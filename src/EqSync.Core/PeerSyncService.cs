namespace EqSync.Core;

public sealed record PeerSyncApplyResult(
    BackupResult LocalBackup,
    int DownloadedFiles,
    int UploadedFiles);

public sealed class PeerSyncService
{
    private readonly IManifestBuilder _manifestBuilder;
    private readonly ISyncPlanner _planner;
    private readonly IPeerTransport _transport;
    private readonly IBackupService _backupService;
    private readonly IRunningProcessGuard _processGuard;
    private readonly string _machineId;
    private readonly string _machineName;
    private readonly string _backupRoot;

    public PeerSyncService(
        IManifestBuilder manifestBuilder,
        ISyncPlanner planner,
        IPeerTransport transport,
        IBackupService backupService,
        IRunningProcessGuard processGuard,
        string machineId,
        string machineName,
        string? backupRoot = null)
    {
        _manifestBuilder = manifestBuilder;
        _planner = planner;
        _transport = transport;
        _backupService = backupService;
        _processGuard = processGuard;
        _machineId = machineId;
        _machineName = machineName;
        _backupRoot = backupRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqSync",
            "backups");
    }

    public async Task<SyncPlan> PreviewAsync(EqInstall localInstall, PeerInfo peer, CancellationToken cancellationToken)
    {
        ThrowIfLocalBlocked();
        await ThrowIfRemoteBlockedAsync(peer, cancellationToken);

        SyncManifest localManifest = _manifestBuilder.Build(localInstall, _machineId, _machineName);
        SyncManifest remoteManifest = await _transport.GetManifestAsync(peer, localInstall.ProfileType, cancellationToken);
        return _planner.Plan(localManifest, remoteManifest);
    }

    public async Task<PeerSyncApplyResult> ApplyAsync(EqInstall localInstall, PeerInfo peer, SyncPlan plan, CancellationToken cancellationToken)
    {
        if (plan.ConflictCount > 0)
        {
            throw new InvalidOperationException("Resolve conflicts before applying sync.");
        }

        ThrowIfLocalBlocked();
        await ThrowIfRemoteBlockedAsync(peer, cancellationToken);

        SyncPlanItem[] incoming = plan.Items.Where(item => item.Action == SyncActionKind.CopyRemoteToLocal).ToArray();
        SyncPlanItem[] outgoing = plan.Items.Where(item => item.Action == SyncActionKind.CopyLocalToRemote).ToArray();

        BackupResult backup = _backupService.BackupFiles(localInstall.Path, incoming.Select(item => item.RelativePath), _backupRoot);

        foreach (SyncPlanItem item in incoming)
        {
            string destination = BackupService.ResolveUnderRoot(localInstall.Path, item.RelativePath);
            await _transport.DownloadFileAsync(peer, localInstall.ProfileType, item.RelativePath, destination, cancellationToken);
            if (item.Remote is not null)
            {
                File.SetLastWriteTimeUtc(destination, item.Remote.LastWriteUtc.UtcDateTime);
            }
        }

        foreach (SyncPlanItem item in outgoing)
        {
            string source = BackupService.ResolveUnderRoot(localInstall.Path, item.RelativePath);
            await _transport.UploadFileAsync(peer, localInstall.ProfileType, item.RelativePath, source, cancellationToken);
        }

        return new PeerSyncApplyResult(backup, incoming.Length, outgoing.Length);
    }

    private void ThrowIfLocalBlocked()
    {
        IReadOnlyList<string> blockers = _processGuard.GetBlockingProcesses();
        if (blockers.Count > 0)
        {
            throw new InvalidOperationException($"Sync blocked locally while running: {string.Join(", ", blockers)}");
        }
    }

    private async Task ThrowIfRemoteBlockedAsync(PeerInfo peer, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> blockers = await _transport.GetBlockingProcessesAsync(peer, cancellationToken);
        if (blockers.Count > 0)
        {
            throw new InvalidOperationException($"Sync blocked on {peer.MachineName} while running: {string.Join(", ", blockers)}");
        }
    }
}
