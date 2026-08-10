namespace EqSync.Core;

public interface ISyncPlanner
{
    SyncPlan Plan(SyncManifest local, SyncManifest remote);
}

public sealed class SyncPlanner : ISyncPlanner
{
    public SyncPlan Plan(SyncManifest local, SyncManifest remote)
    {
        if (local.ProfileType != remote.ProfileType)
        {
            throw new InvalidOperationException($"Cannot sync {local.ProfileType} with {remote.ProfileType}.");
        }

        Dictionary<string, SyncFileEntry> localFiles = local.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, SyncFileEntry> remoteFiles = remote.Files.ToDictionary(file => file.RelativePath, StringComparer.OrdinalIgnoreCase);
        string[] allPaths = localFiles.Keys.Concat(remoteFiles.Keys).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

        List<SyncPlanItem> items = [];
        foreach (string path in allPaths)
        {
            localFiles.TryGetValue(path, out SyncFileEntry? localFile);
            remoteFiles.TryGetValue(path, out SyncFileEntry? remoteFile);
            items.Add(PlanFile(path, localFile, remoteFile));
        }

        return new SyncPlan(local.ProfileType, items);
    }

    private static SyncPlanItem PlanFile(string path, SyncFileEntry? local, SyncFileEntry? remote)
    {
        if (local is null)
        {
            return new SyncPlanItem(path, SyncActionKind.CopyRemoteToLocal, null, remote, "Remote only");
        }

        if (remote is null)
        {
            return new SyncPlanItem(path, SyncActionKind.CopyLocalToRemote, local, null, "Local only");
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(local.Sha256, remote.Sha256))
        {
            return new SyncPlanItem(path, SyncActionKind.NoOp, local, remote, "Same content");
        }

        if (local.LastWriteUtc > remote.LastWriteUtc)
        {
            return new SyncPlanItem(path, SyncActionKind.CopyLocalToRemote, local, remote, "Local newer");
        }

        if (remote.LastWriteUtc > local.LastWriteUtc)
        {
            return new SyncPlanItem(path, SyncActionKind.CopyRemoteToLocal, local, remote, "Remote newer");
        }

        return new SyncPlanItem(path, SyncActionKind.Conflict, local, remote, "Same timestamp, different content");
    }
}
