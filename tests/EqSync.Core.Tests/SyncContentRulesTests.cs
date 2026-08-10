using EqSync.Core;

namespace EqSync.Core.Tests;

public sealed class SyncContentRulesTests
{
    private readonly SyncContentRules _rules = new();

    [Theory]
    [InlineData("eqclient.ini")]
    [InlineData("UI_Avoid_erudin_LO1.ini")]
    [InlineData("Avoid_erudin_LO1.ini")]
    [InlineData("steveiwonder_characters.ini")]
    [InlineData("AutoChannels.txt")]
    [InlineData("userdata/AddressBook.txt")]
    [InlineData("AudioTriggers/default/trigger.wav")]
    [InlineData("maps/poknowledge.txt")]
    [InlineData("uifiles/custom/EQUI_Inventory.xml")]
    public void ShouldSyncFile_IncludesUserOwnedSettings(string relativePath)
    {
        string root = CreateRoot();
        string path = MakeFile(root, relativePath);

        Assert.True(_rules.ShouldSyncFile(root, path));
    }

    [Theory]
    [InlineData("spells_us.txt")]
    [InlineData("spells_us_str.txt")]
    [InlineData("dbstr_us.txt")]
    [InlineData("qeynos_chr.txt")]
    [InlineData("qeynos_EnvironmentEmitters.txt")]
    [InlineData("Logs/dbg.txt")]
    [InlineData("GPUCache/cache.bin")]
    [InlineData("sounds/music.mp3")]
    public void ShouldSyncFile_ExcludesPatchManagedFiles(string relativePath)
    {
        string root = CreateRoot();
        string path = MakeFile(root, relativePath);

        Assert.False(_rules.ShouldSyncFile(root, path));
    }

    private static string CreateRoot()
    {
        return Directory.CreateTempSubdirectory("eqsync-rules-").FullName;
    }

    private static string MakeFile(string root, string relativePath)
    {
        string path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "x");
        return path;
    }
}
