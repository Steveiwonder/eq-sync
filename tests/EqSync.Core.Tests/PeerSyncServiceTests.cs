using EqSync.Core;

namespace EqSync.Core.Tests;

public sealed class PeerSyncServiceTests
{
    [Fact]
    public async Task PreviewAsync_DoesNotBlockWhenRemoteGameIsRunning()
    {
        string local = CreateInstallRoot();
        File.WriteAllText(Path.Combine(local, "eqclient.ini"), "local");

        FakePeerTransport transport = new()
        {
            RemoteBlockers = ["eqgame"],
            RemoteManifest = new SyncManifest(
                "remote",
                "Remote PC",
                EqProfileType.EverQuest,
                "remote-install",
                DateTimeOffset.UtcNow,
                [
                    new SyncFileEntry("eqclient.ini", 6, DateTimeOffset.UtcNow.AddMinutes(1), "remote")
                ])
        };

        PeerSyncService service = new(
            new ManifestBuilder(new SyncContentRules()),
            new SyncPlanner(),
            transport,
            new BackupService(),
            new FakeProcessGuard(),
            "local",
            "pc");

        SyncPlan plan = await service.PreviewAsync(Install(local), Peer(), CancellationToken.None);

        Assert.Equal(0, transport.BlockerRequestCount);
        Assert.Equal(1, plan.ChangeCount);
    }

    [Fact]
    public async Task ApplyAsync_DoesNotBlockWhenOnlyPullingFromRemoteGameThatIsRunning()
    {
        string local = CreateInstallRoot();
        File.WriteAllText(Path.Combine(local, "eqclient.ini"), "old");
        string backup = Directory.CreateTempSubdirectory("eqsync-peer-backup-").FullName;

        SyncPlan plan = new(EqProfileType.EverQuest,
        [
            new SyncPlanItem(
                "eqclient.ini",
                SyncActionKind.CopyRemoteToLocal,
                new SyncFileEntry("eqclient.ini", 3, DateTimeOffset.UtcNow.AddMinutes(-1), "old"),
                new SyncFileEntry("eqclient.ini", 6, DateTimeOffset.UtcNow, "remote"),
                "Remote newer")
        ]);

        FakePeerTransport transport = new()
        {
            RemoteBlockers = ["eqgame"]
        };

        PeerSyncService service = new(
            new ManifestBuilder(new SyncContentRules()),
            new SyncPlanner(),
            transport,
            new BackupService(),
            new FakeProcessGuard(),
            "local",
            "pc",
            backup);

        PeerSyncApplyResult result = await service.ApplyAsync(Install(local), Peer(), plan, CancellationToken.None);

        Assert.Equal(0, transport.BlockerRequestCount);
        Assert.Equal(1, result.DownloadedFiles);
        Assert.Equal("remote", File.ReadAllText(Path.Combine(local, "eqclient.ini")));
    }

    [Fact]
    public async Task ApplyAsync_BlocksWhenWritingToRemoteGameThatIsRunning()
    {
        string local = CreateInstallRoot();
        File.WriteAllText(Path.Combine(local, "eqclient.ini"), "local");

        SyncPlan plan = new(EqProfileType.EverQuest,
        [
            new SyncPlanItem(
                "eqclient.ini",
                SyncActionKind.CopyLocalToRemote,
                new SyncFileEntry("eqclient.ini", 5, DateTimeOffset.UtcNow, "local"),
                null,
                "Local only")
        ]);

        FakePeerTransport transport = new()
        {
            RemoteBlockers = ["eqgame"]
        };

        PeerSyncService service = new(
            new ManifestBuilder(new SyncContentRules()),
            new SyncPlanner(),
            transport,
            new BackupService(),
            new FakeProcessGuard(),
            "local",
            "pc");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyAsync(Install(local), Peer(), plan, CancellationToken.None));

        Assert.Contains("write to", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Remote PC", ex.Message, StringComparison.Ordinal);
        Assert.Equal(1, transport.BlockerRequestCount);
    }

    [Fact]
    public async Task ApplyAsync_DownloadsIncomingAndUploadsOutgoing()
    {
        string local = CreateInstallRoot();
        File.WriteAllText(Path.Combine(local, "eqclient.ini"), "old");
        File.WriteAllText(Path.Combine(local, "notes.txt"), "local");
        string backup = Directory.CreateTempSubdirectory("eqsync-peer-backup-").FullName;

        SyncPlan plan = new(EqProfileType.EverQuest,
        [
            new SyncPlanItem(
                "eqclient.ini",
                SyncActionKind.CopyRemoteToLocal,
                new SyncFileEntry("eqclient.ini", 3, DateTimeOffset.UtcNow.AddMinutes(-1), "old"),
                new SyncFileEntry("eqclient.ini", 3, DateTimeOffset.UtcNow, "new"),
                "Remote newer"),
            new SyncPlanItem(
                "notes.txt",
                SyncActionKind.CopyLocalToRemote,
                new SyncFileEntry("notes.txt", 5, DateTimeOffset.UtcNow, "local"),
                null,
                "Local only")
        ]);

        FakePeerTransport transport = new();
        PeerSyncService service = new(
            new ManifestBuilder(new SyncContentRules()),
            new SyncPlanner(),
            transport,
            new BackupService(),
            new FakeProcessGuard(),
            "local",
            "pc",
            backup);

        List<SyncProgressUpdate> updates = [];
        PeerSyncApplyResult result = await service.ApplyAsync(
            Install(local),
            Peer(),
            plan,
            CancellationToken.None,
            new Progress<SyncProgressUpdate>(updates.Add));

        Assert.Equal("remote", File.ReadAllText(Path.Combine(local, "eqclient.ini")));
        Assert.Contains("notes.txt", transport.Uploads);
        Assert.Equal(1, result.DownloadedFiles);
        Assert.Equal(1, result.UploadedFiles);
        Assert.Equal(1, result.LocalBackup.FileCount);
        Assert.Contains(updates, update => update.Phase == SyncProgressPhase.Downloading && update.RelativePath == "eqclient.ini");
        Assert.Contains(updates, update => update.Phase == SyncProgressPhase.Uploading && update.RelativePath == "notes.txt");
        Assert.Contains(updates, update => update.CompletedItems == 2 && update.TotalItems == 2);
    }

    private static string CreateInstallRoot()
    {
        string root = Directory.CreateTempSubdirectory("eqsync-peer-install-").FullName;
        File.WriteAllText(Path.Combine(root, "eqgame.exe"), string.Empty);
        return root;
    }

    private static EqInstall Install(string root)
    {
        return new EqInstall("install", "EverQuest", EqProfileType.EverQuest, root, EqInstallDetectionSource.Manual);
    }

    private static PeerInfo Peer()
    {
        return new PeerInfo("remote", "Remote PC", "1.0", new Uri("http://remote/"), true, [EqProfileType.EverQuest]);
    }

    private sealed class FakePeerTransport : IPeerTransport
    {
        public List<string> Uploads { get; } = [];
        public IReadOnlyList<string> RemoteBlockers { get; init; } = [];
        public SyncManifest? RemoteManifest { get; init; }
        public int BlockerRequestCount { get; private set; }

        public Task<SyncManifest> GetManifestAsync(PeerInfo peer, EqProfileType profileType, CancellationToken cancellationToken)
        {
            if (RemoteManifest is null)
            {
                throw new NotSupportedException();
            }

            return Task.FromResult(RemoteManifest);
        }

        public Task<IReadOnlyList<string>> GetBlockingProcessesAsync(PeerInfo peer, CancellationToken cancellationToken)
        {
            BlockerRequestCount++;
            return Task.FromResult(RemoteBlockers);
        }

        public Task DownloadFileAsync(PeerInfo peer, EqProfileType profileType, string relativePath, string destinationPath, CancellationToken cancellationToken)
        {
            File.WriteAllText(destinationPath, "remote");
            return Task.CompletedTask;
        }

        public Task UploadFileAsync(PeerInfo peer, EqProfileType profileType, string relativePath, string sourcePath, CancellationToken cancellationToken)
        {
            Uploads.Add(relativePath);
            Assert.True(File.Exists(sourcePath));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProcessGuard : IRunningProcessGuard
    {
        public bool IsSyncBlocked() => false;

        public IReadOnlyList<string> GetBlockingProcesses() => [];
    }
}
