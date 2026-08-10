using System.Net;
using System.Text.Json;

namespace EqSync.Core;

public sealed class PeerHttpService : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly HttpListener _listener = new();
    private readonly Func<IReadOnlyList<EqInstall>> _installProvider;
    private readonly IManifestBuilder _manifestBuilder;
    private readonly ISyncContentRules _syncContentRules;
    private readonly IBackupService _backupService;
    private readonly IRunningProcessGuard _processGuard;
    private readonly string _machineId;
    private readonly string _machineName;
    private readonly string _backupRoot;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public Uri Endpoint { get; }

    public PeerHttpService(
        Func<IReadOnlyList<EqInstall>> installProvider,
        IManifestBuilder manifestBuilder,
        ISyncContentRules syncContentRules,
        IBackupService backupService,
        IRunningProcessGuard processGuard,
        string machineId,
        string machineName,
        string? backupRoot = null,
        int port = 47642)
    {
        _installProvider = installProvider;
        _manifestBuilder = manifestBuilder;
        _syncContentRules = syncContentRules;
        _backupService = backupService;
        _processGuard = processGuard;
        _machineId = machineId;
        _machineName = machineName;
        _backupRoot = backupRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqSync",
            "backups");
        Endpoint = new Uri($"http://{Environment.MachineName}:{port}/");
        _listener.Prefixes.Add($"http://+:{port}/");
    }

    public void Start()
    {
        if (_listenTask is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _listener.Start();
        _listenTask = Task.Run(() => ListenAsync(_cts.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (_cts is not null)
        {
            await _cts.CancelAsync();
        }

        if (_listener.IsListening)
        {
            _listener.Stop();
        }

        if (_listenTask is not null)
        {
            await _listenTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }

        _listener.Close();
        _cts?.Dispose();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (HttpListenerException)
            {
                break;
            }

            _ = Task.Run(() => HandleAsync(context), cancellationToken);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            string path = context.Request.Url?.AbsolutePath.Trim('/') ?? string.Empty;
            if (string.Equals(path, "health", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, new { ok = true });
                return;
            }

            if (string.Equals(path, "manifest", StringComparison.OrdinalIgnoreCase))
            {
                await WriteManifestAsync(context);
                return;
            }

            if (string.Equals(path, "blockers", StringComparison.OrdinalIgnoreCase))
            {
                await WriteJsonAsync(context.Response, _processGuard.GetBlockingProcesses());
                return;
            }

            if (string.Equals(path, "file", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "GET")
            {
                await DownloadFileAsync(context);
                return;
            }

            if (string.Equals(path, "file", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod == "PUT")
            {
                await UploadFileAsync(context);
                return;
            }

            context.Response.StatusCode = 404;
        }
        catch
        {
            context.Response.StatusCode = 500;
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task WriteManifestAsync(HttpListenerContext context)
    {
        string? profileValue = context.Request.QueryString["profile"];
        if (!Enum.TryParse(profileValue, ignoreCase: true, out EqProfileType profileType))
        {
            context.Response.StatusCode = 400;
            return;
        }

        EqInstall? install = _installProvider().FirstOrDefault(candidate => candidate.ProfileType == profileType);
        if (install is null)
        {
            context.Response.StatusCode = 404;
            return;
        }

        SyncManifest manifest = _manifestBuilder.Build(install, _machineId, _machineName);
        await WriteJsonAsync(context.Response, manifest);
    }

    private async Task DownloadFileAsync(HttpListenerContext context)
    {
        EqInstall? install = ResolveInstall(context);
        string? relativePath = context.Request.QueryString["path"];
        if (install is null || string.IsNullOrWhiteSpace(relativePath))
        {
            context.Response.StatusCode = 400;
            return;
        }

        string filePath = BackupService.ResolveUnderRoot(install.Path, relativePath);
        if (!_syncContentRules.ShouldSyncFile(install.Path, filePath) || !File.Exists(filePath))
        {
            context.Response.StatusCode = 404;
            return;
        }

        context.Response.ContentType = "application/octet-stream";
        await using FileStream source = File.OpenRead(filePath);
        context.Response.ContentLength64 = source.Length;
        await source.CopyToAsync(context.Response.OutputStream);
    }

    private async Task UploadFileAsync(HttpListenerContext context)
    {
        if (_processGuard.IsSyncBlocked())
        {
            context.Response.StatusCode = 409;
            await WriteJsonAsync(context.Response, _processGuard.GetBlockingProcesses());
            return;
        }

        EqInstall? install = ResolveInstall(context);
        string? relativePath = context.Request.QueryString["path"];
        if (install is null || string.IsNullOrWhiteSpace(relativePath))
        {
            context.Response.StatusCode = 400;
            return;
        }

        string destination = BackupService.ResolveUnderRoot(install.Path, relativePath);
        if (!_syncContentRules.ShouldSyncFile(install.Path, destination))
        {
            context.Response.StatusCode = 403;
            return;
        }

        _backupService.BackupFiles(install.Path, [relativePath], _backupRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string tempPath = destination + ".eqsync.tmp";
        await using (FileStream temp = File.Create(tempPath))
        {
            await context.Request.InputStream.CopyToAsync(temp);
        }

        File.Move(tempPath, destination, overwrite: true);
        if (DateTimeOffset.TryParse(context.Request.QueryString["lastWriteUtc"], out DateTimeOffset lastWriteUtc))
        {
            File.SetLastWriteTimeUtc(destination, lastWriteUtc.UtcDateTime);
        }

        context.Response.StatusCode = 204;
    }

    private EqInstall? ResolveInstall(HttpListenerContext context)
    {
        string? profileValue = context.Request.QueryString["profile"];
        if (!Enum.TryParse(profileValue, ignoreCase: true, out EqProfileType profileType))
        {
            return null;
        }

        return _installProvider().FirstOrDefault(candidate => candidate.ProfileType == profileType);
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, object value)
    {
        response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(response.OutputStream, value, JsonOptions);
    }
}

public sealed class PeerHttpTransport : IPeerTransport
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;

    public PeerHttpTransport(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<SyncManifest> GetManifestAsync(PeerInfo peer, EqProfileType profileType, CancellationToken cancellationToken)
    {
        Uri uri = new(peer.Endpoint, $"manifest?profile={Uri.EscapeDataString(profileType.ToString())}");
        using Stream stream = await _httpClient.GetStreamAsync(uri, cancellationToken);
        return await JsonSerializer.DeserializeAsync<SyncManifest>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Peer returned an empty manifest.");
    }

    public async Task<IReadOnlyList<string>> GetBlockingProcessesAsync(PeerInfo peer, CancellationToken cancellationToken)
    {
        Uri uri = new(peer.Endpoint, "blockers");
        using Stream stream = await _httpClient.GetStreamAsync(uri, cancellationToken);
        return await JsonSerializer.DeserializeAsync<IReadOnlyList<string>>(stream, JsonOptions, cancellationToken) ?? [];
    }

    public async Task DownloadFileAsync(PeerInfo peer, EqProfileType profileType, string relativePath, string destinationPath, CancellationToken cancellationToken)
    {
        Uri uri = BuildFileUri(peer, profileType, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        await using Stream source = await _httpClient.GetStreamAsync(uri, cancellationToken);
        await using FileStream destination = File.Create(destinationPath);
        await source.CopyToAsync(destination, cancellationToken);
    }

    public async Task UploadFileAsync(PeerInfo peer, EqProfileType profileType, string relativePath, string sourcePath, CancellationToken cancellationToken)
    {
        DateTimeOffset lastWriteUtc = File.GetLastWriteTimeUtc(sourcePath);
        Uri uri = BuildFileUri(peer, profileType, relativePath, lastWriteUtc);
        await using FileStream source = File.OpenRead(sourcePath);
        using StreamContent content = new(source);
        using HttpResponseMessage response = await _httpClient.PutAsync(uri, content, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static Uri BuildFileUri(PeerInfo peer, EqProfileType profileType, string relativePath, DateTimeOffset? lastWriteUtc = null)
    {
        string query = $"profile={Uri.EscapeDataString(profileType.ToString())}&path={Uri.EscapeDataString(relativePath)}";
        if (lastWriteUtc is not null)
        {
            query += $"&lastWriteUtc={Uri.EscapeDataString(lastWriteUtc.Value.UtcDateTime.ToString("O"))}";
        }

        return new Uri(peer.Endpoint, $"file?{query}");
    }
}
