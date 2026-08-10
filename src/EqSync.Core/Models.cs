namespace EqSync.Core;

public enum EqProfileType
{
    EverQuest,
    EverQuestLegends
}

public enum EqInstallDetectionSource
{
    Registry,
    CommonPath,
    Manual
}

public sealed record EqInstall(
    string Id,
    string DisplayName,
    EqProfileType ProfileType,
    string Path,
    EqInstallDetectionSource DetectionSource);

public sealed record SyncFileEntry(
    string RelativePath,
    long Size,
    DateTimeOffset LastWriteUtc,
    string Sha256);

public sealed record SyncManifest(
    string MachineId,
    string MachineName,
    EqProfileType ProfileType,
    string InstallId,
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyList<SyncFileEntry> Files);

public enum SyncActionKind
{
    NoOp,
    CopyLocalToRemote,
    CopyRemoteToLocal,
    Conflict
}

public sealed record SyncPlanItem(
    string RelativePath,
    SyncActionKind Action,
    SyncFileEntry? Local,
    SyncFileEntry? Remote,
    string Reason);

public sealed record SyncPlan(
    EqProfileType ProfileType,
    IReadOnlyList<SyncPlanItem> Items)
{
    public int ChangeCount => Items.Count(item => item.Action is not SyncActionKind.NoOp);

    public int ConflictCount => Items.Count(item => item.Action == SyncActionKind.Conflict);
}

public sealed record PeerInfo(
    string MachineId,
    string MachineName,
    string AppVersion,
    Uri Endpoint,
    bool IsPaired,
    IReadOnlyList<EqProfileType> Profiles);

public sealed record BackupResult(string BackupPath, int FileCount);
