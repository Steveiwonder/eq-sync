namespace EqSync.Core;

public interface ISyncApplier
{
    BackupResult ApplyRemoteToLocal(string localInstallRoot, string remoteInstallRoot, SyncPlan plan, string backupRoot);
}

public sealed class SyncApplier : ISyncApplier
{
    private readonly IBackupService _backupService;

    public SyncApplier(IBackupService backupService)
    {
        _backupService = backupService;
    }

    public BackupResult ApplyRemoteToLocal(string localInstallRoot, string remoteInstallRoot, SyncPlan plan, string backupRoot)
    {
        SyncPlanItem[] incoming = plan.Items
            .Where(item => item.Action == SyncActionKind.CopyRemoteToLocal)
            .ToArray();

        BackupResult backup = _backupService.BackupFiles(localInstallRoot, incoming.Select(item => item.RelativePath), backupRoot);

        foreach (SyncPlanItem item in incoming)
        {
            string source = BackupService.ResolveUnderRoot(remoteInstallRoot, item.RelativePath);
            string destination = BackupService.ResolveUnderRoot(localInstallRoot, item.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        return backup;
    }
}
