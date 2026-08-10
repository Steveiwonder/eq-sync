using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace EqSync.App;

internal sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string? LatestVersion,
    Uri? ReleasePageUrl,
    Uri? DownloadUrl);

internal sealed class SelfUpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Steveiwonder/eq-sync/releases/latest";
    private const string ReleaseAssetName = "EqSync-win-x64.zip";
    private readonly HttpClient _httpClient;

    public SelfUpdateService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("EqSync-Updater");
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync(CancellationToken cancellationToken)
    {
        using Stream stream = await _httpClient.GetStreamAsync(LatestReleaseUrl, cancellationToken);
        using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        JsonElement root = document.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        string latestVersion = tag.TrimStart('v', 'V');
        Uri releasePageUrl = new(root.GetProperty("html_url").GetString() ?? "https://github.com/Steveiwonder/eq-sync/releases/latest");
        Uri? downloadUrl = FindAssetDownloadUrl(root);
        string currentVersion = AppVersionProvider.Current;

        return new UpdateCheckResult(
            IsNewer(latestVersion, currentVersion) && downloadUrl is not null,
            currentVersion,
            latestVersion,
            releasePageUrl,
            downloadUrl);
    }

    public async Task DownloadAndLaunchUpdaterAsync(UpdateCheckResult update, CancellationToken cancellationToken)
    {
        if (update.DownloadUrl is null)
        {
            throw new InvalidOperationException("The latest release does not contain the Windows zip asset.");
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "EqSync", "updates", Guid.NewGuid().ToString("N"));
        string zipPath = Path.Combine(tempRoot, ReleaseAssetName);
        string extractPath = Path.Combine(tempRoot, "extract");
        Directory.CreateDirectory(tempRoot);

        await using (Stream source = await _httpClient.GetStreamAsync(update.DownloadUrl, cancellationToken))
        await using (FileStream destination = File.Create(zipPath))
        {
            await source.CopyToAsync(destination, cancellationToken);
        }

        ZipFile.ExtractToDirectory(zipPath, extractPath, overwriteFiles: true);
        string appDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        string executablePath = Environment.ProcessPath ?? Path.Combine(appDirectory, "EqSync.App.exe");
        string scriptPath = WriteUpdaterScript(tempRoot, extractPath, appDirectory, executablePath, Environment.ProcessId);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static Uri? FindAssetDownloadUrl(JsonElement release)
    {
        foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
        {
            string? name = asset.GetProperty("name").GetString();
            if (!string.Equals(name, ReleaseAssetName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string? url = asset.GetProperty("browser_download_url").GetString();
            return string.IsNullOrWhiteSpace(url) ? null : new Uri(url);
        }

        return null;
    }

    private static bool IsNewer(string latestVersion, string currentVersion)
    {
        return TryParseVersion(latestVersion, out Version? latest) &&
            TryParseVersion(currentVersion, out Version? current) &&
            latest > current;
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        string normalized = value.Split('+', '-')[0];
        return Version.TryParse(normalized, out version);
    }

    private static string WriteUpdaterScript(string tempRoot, string extractPath, string appDirectory, string executablePath, int processId)
    {
        string scriptPath = Path.Combine(tempRoot, "apply-update.ps1");
        string script = $$"""
        $ErrorActionPreference = 'Stop'
        $processId = {{processId}}
        $extractPath = '{{EscapePowerShellString(extractPath)}}'
        $appDirectory = '{{EscapePowerShellString(appDirectory)}}'
        $executablePath = '{{EscapePowerShellString(executablePath)}}'
        $tempRoot = '{{EscapePowerShellString(tempRoot)}}'

        try {
          Wait-Process -Id $processId -Timeout 30 -ErrorAction SilentlyContinue
        } catch {
        }

        Get-ChildItem -LiteralPath $extractPath -Force | ForEach-Object {
          Copy-Item -LiteralPath $_.FullName -Destination $appDirectory -Recurse -Force
        }

        Start-Process -FilePath $executablePath -WorkingDirectory $appDirectory
        Start-Sleep -Seconds 2
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        """;
        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string EscapePowerShellString(string value)
    {
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
