using System.Security.Cryptography;

namespace EqSync.Core;

public interface IManifestBuilder
{
    SyncManifest Build(EqInstall install, string machineId, string machineName);
}

public sealed class ManifestBuilder : IManifestBuilder
{
    private readonly ISyncContentRules _rules;
    private readonly IEqSyncLogger _logger;

    public ManifestBuilder(ISyncContentRules rules, IEqSyncLogger? logger = null)
    {
        _rules = rules;
        _logger = logger ?? NullEqSyncLogger.Instance;
    }

    public SyncManifest Build(EqInstall install, string machineId, string machineName)
    {
        List<SyncFileEntry> files = [];
        int scannedFiles = 0;
        int skippedFiles = 0;
        _logger.Info($"Building manifest. Profile={install.ProfileType}; Name={install.DisplayName}; Path={install.Path}; Machine={machineName}; InstallId={install.Id}");
        foreach (string file in Directory.EnumerateFiles(install.Path, "*", SearchOption.AllDirectories))
        {
            scannedFiles++;
            if (!_rules.ShouldSyncFile(install.Path, file))
            {
                skippedFiles++;
                continue;
            }

            FileInfo info = new(file);
            files.Add(new SyncFileEntry(
                NormalizeRelative(Path.GetRelativePath(install.Path, file)),
                info.Length,
                info.LastWriteTimeUtc,
                ComputeSha256(file)));
        }

        _logger.Info($"Manifest built. Profile={install.ProfileType}; Tracked={files.Count}; Scanned={scannedFiles}; Skipped={skippedFiles}; Path={install.Path}");
        return new SyncManifest(
            machineId,
            machineName,
            install.ProfileType,
            install.Id,
            DateTimeOffset.UtcNow,
            files.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string NormalizeRelative(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
    }
}
