namespace EqSync.Core;

public interface IBackupService
{
    BackupResult BackupFiles(string installRoot, IEnumerable<string> relativePaths, string backupRoot);
}

public sealed class BackupService : IBackupService
{
    public BackupResult BackupFiles(string installRoot, IEnumerable<string> relativePaths, string backupRoot)
    {
        string destinationRoot = Path.Combine(backupRoot, DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss"));
        int count = 0;

        foreach (string relativePath in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string source = ResolveUnderRoot(installRoot, relativePath);
            if (!File.Exists(source))
            {
                continue;
            }

            string destination = ResolveUnderRoot(destinationRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
            count++;
        }

        return new BackupResult(destinationRoot, count);
    }

    internal static string ResolveUnderRoot(string root, string relativePath)
    {
        string fullRoot = Path.GetFullPath(root);
        string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!fullPath.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !StringComparer.OrdinalIgnoreCase.Equals(fullPath, fullRoot))
        {
            throw new InvalidOperationException($"Path escapes root: {relativePath}");
        }

        return fullPath;
    }
}
