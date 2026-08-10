using System.Security.Cryptography;

namespace EqSync.Core;

public interface IManifestBuilder
{
    SyncManifest Build(EqInstall install, string machineId, string machineName);
}

public sealed class ManifestBuilder : IManifestBuilder
{
    private readonly ISyncContentRules _rules;

    public ManifestBuilder(ISyncContentRules rules)
    {
        _rules = rules;
    }

    public SyncManifest Build(EqInstall install, string machineId, string machineName)
    {
        List<SyncFileEntry> files = [];
        foreach (string file in Directory.EnumerateFiles(install.Path, "*", SearchOption.AllDirectories))
        {
            if (!_rules.ShouldSyncFile(install.Path, file))
            {
                continue;
            }

            FileInfo info = new(file);
            files.Add(new SyncFileEntry(
                NormalizeRelative(Path.GetRelativePath(install.Path, file)),
                info.Length,
                info.LastWriteTimeUtc,
                ComputeSha256(file)));
        }

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
