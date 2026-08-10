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
    private readonly IEqSyncLogger _logger;
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
        string? backupRoot = null,
        IEqSyncLogger? logger = null)
    {
        _manifestBuilder = manifestBuilder;
        _planner = planner;
        _transport = transport;
        _backupService = backupService;
        _processGuard = processGuard;
        _machineId = machineId;
        _machineName = machineName;
        _logger = logger ?? NullEqSyncLogger.Instance;
        _backupRoot = backupRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "EqSync",
            "backups");
    }

    public async Task<SyncPlan> PreviewAsync(EqInstall localInstall, PeerInfo peer, CancellationToken cancellationToken)
    {
        _logger.Info($"Preview requested. LocalProfile={localInstall.ProfileType}; LocalPath={localInstall.Path}; Peer={peer.MachineName}; PeerEndpoint={peer.Endpoint}; PeerVersion={peer.AppVersion}");
        ThrowIfLocalBlocked();
        await ThrowIfRemoteBlockedAsync(peer, cancellationToken);

        SyncManifest localManifest = _manifestBuilder.Build(localInstall, _machineId, _machineName);
        _logger.Info($"Local manifest ready. Profile={localManifest.ProfileType}; Files={localManifest.Files.Count}; Machine={localManifest.MachineName}");
        SyncManifest remoteManifest = await _transport.GetManifestAsync(peer, localInstall.ProfileType, cancellationToken);
        _logger.Info($"Remote manifest ready. Profile={remoteManifest.ProfileType}; Files={remoteManifest.Files.Count}; Machine={remoteManifest.MachineName}");
        SyncPlan plan = _planner.Plan(localManifest, remoteManifest);
        _logger.Info($"Preview plan ready. Profile={plan.ProfileType}; Items={plan.Items.Count}; Changes={plan.ChangeCount}; Conflicts={plan.ConflictCount}");
        return plan;
    }

    public async Task<PeerSyncApplyResult> ApplyAsync(EqInstall localInstall, PeerInfo peer, SyncPlan plan, CancellationToken cancellationToken)
    {
        _logger.Info($"Apply requested. Profile={localInstall.ProfileType}; Items={plan.Items.Count}; Changes={plan.ChangeCount}; Conflicts={plan.ConflictCount}; Peer={peer.MachineName}");
        if (plan.ConflictCount > 0)
        {
            throw new InvalidOperationException("Resolve conflicts before applying sync.");
        }

        ThrowIfLocalBlocked();
        await ThrowIfRemoteBlockedAsync(peer, cancellationToken);

        SyncPlanItem[] incoming = plan.Items.Where(item => item.Action == SyncActionKind.CopyRemoteToLocal).ToArray();
        SyncPlanItem[] outgoing = plan.Items.Where(item => item.Action == SyncActionKind.CopyLocalToRemote).ToArray();
        _logger.Info($"Apply classified actions. Incoming={incoming.Length}; Outgoing={outgoing.Length}");

        BackupResult backup = _backupService.BackupFiles(localInstall.Path, incoming.Select(item => item.RelativePath), _backupRoot);
        _logger.Info($"Local backup complete. Path={backup.BackupPath}; Files={backup.FileCount}");

        foreach (SyncPlanItem item in incoming)
        {
            string destination = BackupService.ResolveUnderRoot(localInstall.Path, item.RelativePath);
            _logger.Info($"Downloading file from peer. Path={item.RelativePath}; Destination={destination}");
            await _transport.DownloadFileAsync(peer, localInstall.ProfileType, item.RelativePath, destination, cancellationToken);
            if (item.Remote is not null)
            {
                File.SetLastWriteTimeUtc(destination, item.Remote.LastWriteUtc.UtcDateTime);
            }
        }

        foreach (SyncPlanItem item in outgoing)
        {
            string source = BackupService.ResolveUnderRoot(localInstall.Path, item.RelativePath);
            _logger.Info($"Uploading file to peer. Path={item.RelativePath}; Source={source}");
            await _transport.UploadFileAsync(peer, localInstall.ProfileType, item.RelativePath, source, cancellationToken);
        }

        _logger.Info($"Apply complete. Downloaded={incoming.Length}; Uploaded={outgoing.Length}");
        return new PeerSyncApplyResult(backup, incoming.Length, outgoing.Length);
    }

    private void ThrowIfLocalBlocked()
    {
        IReadOnlyList<string> blockers = _processGuard.GetBlockingProcesses();
        if (blockers.Count > 0)
        {
            _logger.Info($"Local sync blockers detected. Processes={string.Join(", ", blockers)}");
            throw new InvalidOperationException($"Sync blocked locally while running: {string.Join(", ", blockers)}");
        }
    }

    private async Task ThrowIfRemoteBlockedAsync(PeerInfo peer, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> blockers = await _transport.GetBlockingProcessesAsync(peer, cancellationToken);
        if (blockers.Count > 0)
        {
            _logger.Info($"Remote sync blockers detected. Peer={peer.MachineName}; Processes={string.Join(", ", blockers)}");
            throw new InvalidOperationException($"Sync blocked on {peer.MachineName} while running: {string.Join(", ", blockers)}");
        }
    }
}
