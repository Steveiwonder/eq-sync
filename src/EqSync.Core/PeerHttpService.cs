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
    private readonly string _machineId;
    private readonly string _machineName;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public Uri Endpoint { get; }

    public PeerHttpService(
        Func<IReadOnlyList<EqInstall>> installProvider,
        IManifestBuilder manifestBuilder,
        string machineId,
        string machineName,
        int port = 47642)
    {
        _installProvider = installProvider;
        _manifestBuilder = manifestBuilder;
        _machineId = machineId;
        _machineName = machineName;
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
}
