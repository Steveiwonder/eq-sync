using EqSync.Core;

namespace EqSync.Core.Tests;

public sealed class ManifestAndApplyTests
{
    [Fact]
    public void BuildManifest_TracksOnlyAllowedFiles()
    {
        string root = CreateInstallRoot();
        File.WriteAllText(Path.Combine(root, "eqclient.ini"), "settings");
        File.WriteAllText(Path.Combine(root, "spells_us.txt"), "patch");
        Directory.CreateDirectory(Path.Combine(root, "userdata"));
        File.WriteAllText(Path.Combine(root, "userdata", "AddressBook.txt"), "names");

        ManifestBuilder builder = new(new SyncContentRules());
        SyncManifest manifest = builder.Build(Install(root), "machine", "pc");

        Assert.Contains(manifest.Files, file => file.RelativePath == "eqclient.ini");
        Assert.Contains(manifest.Files, file => file.RelativePath == "userdata/AddressBook.txt");
        Assert.DoesNotContain(manifest.Files, file => file.RelativePath == "spells_us.txt");
    }

    [Fact]
    public void ApplyRemoteToLocal_BacksUpBeforeOverwrite()
    {
        string local = CreateInstallRoot();
        string remote = CreateInstallRoot();
        string backup = Directory.CreateTempSubdirectory("eqsync-backup-").FullName;
        File.WriteAllText(Path.Combine(local, "eqclient.ini"), "old");
        File.WriteAllText(Path.Combine(remote, "eqclient.ini"), "new");

        SyncPlan plan = new(EqProfileType.EverQuest,
        [
            new SyncPlanItem(
                "eqclient.ini",
                SyncActionKind.CopyRemoteToLocal,
                new SyncFileEntry("eqclient.ini", 3, DateTimeOffset.UtcNow.AddMinutes(-1), "old"),
                new SyncFileEntry("eqclient.ini", 3, DateTimeOffset.UtcNow, "new"),
                "Remote newer")
        ]);

        SyncApplier applier = new(new BackupService());
        BackupResult result = applier.ApplyRemoteToLocal(local, remote, plan, backup);

        Assert.Equal("new", File.ReadAllText(Path.Combine(local, "eqclient.ini")));
        Assert.Equal(1, result.FileCount);
        Assert.True(File.Exists(Path.Combine(result.BackupPath, "eqclient.ini")));
    }

    private static string CreateInstallRoot()
    {
        string root = Directory.CreateTempSubdirectory("eqsync-install-").FullName;
        File.WriteAllText(Path.Combine(root, "eqgame.exe"), string.Empty);
        return root;
    }

    private static EqInstall Install(string root)
    {
        return new EqInstall("install", "EverQuest", EqProfileType.EverQuest, root, EqInstallDetectionSource.Manual);
    }
}
