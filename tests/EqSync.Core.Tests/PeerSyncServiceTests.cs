using EqSync.Core;

namespace EqSync.Core.Tests;

public sealed class PeerSyncServiceTests
{
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

        PeerSyncApplyResult result = await service.ApplyAsync(Install(local), Peer(), plan, CancellationToken.None);

        Assert.Equal("remote", File.ReadAllText(Path.Combine(local, "eqclient.ini")));
        Assert.Contains("notes.txt", transport.Uploads);
        Assert.Equal(1, result.DownloadedFiles);
        Assert.Equal(1, result.UploadedFiles);
        Assert.Equal(1, result.LocalBackup.FileCount);
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

        public Task<SyncManifest> GetManifestAsync(PeerInfo peer, EqProfileType profileType, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<IReadOnlyList<string>> GetBlockingProcessesAsync(PeerInfo peer, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
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
