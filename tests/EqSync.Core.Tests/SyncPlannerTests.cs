using EqSync.Core;

namespace EqSync.Core.Tests;

public sealed class SyncPlannerTests
{
    private readonly SyncPlanner _planner = new();

    [Fact]
    public void Plan_CopiesMissingFilesBothDirections()
    {
        SyncManifest local = Manifest([Entry("local.ini", 2)]);
        SyncManifest remote = Manifest([Entry("remote.ini", 3)], machineId: "remote");

        SyncPlan plan = _planner.Plan(local, remote);

        Assert.Contains(plan.Items, item => item.RelativePath == "local.ini" && item.Action == SyncActionKind.CopyLocalToRemote);
        Assert.Contains(plan.Items, item => item.RelativePath == "remote.ini" && item.Action == SyncActionKind.CopyRemoteToLocal);
    }

    [Fact]
    public void Plan_ChoosesNewestWhenHashesDiffer()
    {
        SyncManifest local = Manifest([Entry("eqclient.ini", 20, "local")]);
        SyncManifest remote = Manifest([Entry("eqclient.ini", 10, "remote")], machineId: "remote");

        SyncPlan plan = _planner.Plan(local, remote);

        SyncPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(SyncActionKind.CopyLocalToRemote, item.Action);
        Assert.Equal("Local newer", item.Reason);
    }

    [Fact]
    public void Plan_ConflictsWhenSameTimestampDifferentHash()
    {
        SyncManifest local = Manifest([Entry("eqclient.ini", 10, "local")]);
        SyncManifest remote = Manifest([Entry("eqclient.ini", 10, "remote")], machineId: "remote");

        SyncPlan plan = _planner.Plan(local, remote);

        SyncPlanItem item = Assert.Single(plan.Items);
        Assert.Equal(SyncActionKind.Conflict, item.Action);
    }

    private static SyncManifest Manifest(IReadOnlyList<SyncFileEntry> entries, string machineId = "local")
    {
        return new SyncManifest(machineId, machineId, EqProfileType.EverQuest, "install", DateTimeOffset.UtcNow, entries);
    }

    private static SyncFileEntry Entry(string relativePath, int timestampSeconds, string hash = "same")
    {
        return new SyncFileEntry(relativePath, 1, DateTimeOffset.UnixEpoch.AddSeconds(timestampSeconds), hash);
    }
}
