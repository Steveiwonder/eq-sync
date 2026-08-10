using System.Text.RegularExpressions;

namespace EqSync.Core;

public interface ISyncContentRules
{
    bool ShouldSyncFile(string installRoot, string filePath);
}

public sealed partial class SyncContentRules : ISyncContentRules
{
    private static readonly string[] IncludedDirectories =
    [
        "userdata",
        "AudioTriggers",
        "maps",
        "uifiles"
    ];

    private static readonly string[] ExcludedDirectories =
    [
        "Logs",
        "GPUCache",
        "backup",
        "Resources",
        "sounds",
        "voice",
        "SpellEffects",
        "RenderEffects",
        "LaunchPad.libs",
        "ActorEffects",
        "EnvEmitterEffects"
    ];

    public bool ShouldSyncFile(string installRoot, string filePath)
    {
        string relativePath = Path.GetRelativePath(installRoot, filePath);
        if (relativePath.StartsWith("..", StringComparison.Ordinal) || Path.IsPathFullyQualified(relativePath))
        {
            return false;
        }

        string[] parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(part => ExcludedDirectories.Contains(part, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (parts.Length > 1)
        {
            return IncludedDirectories.Contains(parts[0], StringComparer.OrdinalIgnoreCase);
        }

        string name = Path.GetFileName(relativePath);
        if (PatchManagedRootFile().IsMatch(name))
        {
            return false;
        }

        return RootSettingsFile().IsMatch(name);
    }

    [GeneratedRegex(@"^(eqclient|eqlsClient|eqlsPlayerData|eqlsUIConfig|LaunchPad-user)\.ini$|^(AutoChannels|notes)\.txt$|^.+_characters\.ini$|^UI_.+_.+_.+\.ini$|^.+_.+_.+\.ini$|^(BZR|MQUI)_.+\.ini$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RootSettingsFile();

    [GeneratedRegex(@"^.*_chr\.txt$|^.*_EnvironmentEmitters\.txt$|^spells_us.*\.txt$|^(dbstr_us|eqstr_us)\.txt$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PatchManagedRootFile();
}
