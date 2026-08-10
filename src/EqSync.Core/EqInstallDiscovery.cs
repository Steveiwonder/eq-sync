using Microsoft.Win32;
using System.Runtime.Versioning;

namespace EqSync.Core;

[SupportedOSPlatform("windows")]
public sealed class EqInstallDiscovery : IEqInstallDiscovery
{
    private static readonly string[] UninstallRegistryRoots =
    [
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
    ];

    private readonly IReadOnlyList<string> _manualPaths;

    public EqInstallDiscovery(IEnumerable<string>? manualPaths = null)
    {
        _manualPaths = manualPaths?.ToArray() ?? [];
    }

    public IReadOnlyList<EqInstall> Discover()
    {
        Dictionary<string, EqInstall> installs = new(StringComparer.OrdinalIgnoreCase);

        foreach (EqInstall install in DiscoverFromRegistry())
        {
            installs.TryAdd(NormalizePath(install.Path), install);
        }

        foreach (EqInstall install in DiscoverFromCommonPaths())
        {
            installs.TryAdd(NormalizePath(install.Path), install);
        }

        foreach (string manualPath in _manualPaths)
        {
            EqInstall? install = TryCreateInstall(manualPath, EqInstallDetectionSource.Manual);
            if (install is not null)
            {
                installs[NormalizePath(install.Path)] = install;
            }
        }

        return installs.Values
            .OrderBy(install => install.ProfileType)
            .ThenBy(install => install.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<EqInstall> DiscoverFromRegistry()
    {
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            foreach (string rootName in UninstallRegistryRoots)
            {
                using RegistryKey? root = baseKey.OpenSubKey(rootName);
                if (root is null)
                {
                    continue;
                }

                foreach (string subKeyName in root.GetSubKeyNames())
                {
                    using RegistryKey? subKey = root.OpenSubKey(subKeyName);
                    string? displayName = subKey?.GetValue("DisplayName") as string;
                    if (!IsEverQuestName(displayName))
                    {
                        continue;
                    }

                    string? installLocation = subKey?.GetValue("InstallLocation") as string;
                    string? uninstallString = subKey?.GetValue("UninstallString") as string;
                    string? path = FirstExistingInstallPath(installLocation, TryExtractPathFromCommand(uninstallString));
                    if (path is null)
                    {
                        continue;
                    }

                    EqInstall? install = TryCreateInstall(path, EqInstallDetectionSource.Registry, displayName);
                    if (install is not null)
                    {
                        yield return install;
                    }
                }
            }
        }
    }

    private static IEnumerable<EqInstall> DiscoverFromCommonPaths()
    {
        string[] roots =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Daybreak Game Company", "Installed Games"),
            @"C:\Users\Public\Daybreak Game Company\Installed Games",
            @"C:\Daybreak Game Company",
            @"C:\Games",
            @"C:\EverQuest",
            @"C:\EQ"
        ];

        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (string eqGame in Directory.EnumerateFiles(root, "eqgame.exe", SearchOption.AllDirectories).Take(20))
            {
                EqInstall? install = TryCreateInstall(Path.GetDirectoryName(eqGame)!, EqInstallDetectionSource.CommonPath);
                if (install is not null)
                {
                    yield return install;
                }
            }
        }
    }

    private static EqInstall? TryCreateInstall(string path, EqInstallDetectionSource source, string? displayName = null)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim('"')));
        }
        catch
        {
            return null;
        }

        if (!File.Exists(Path.Combine(fullPath, "eqgame.exe")))
        {
            return null;
        }

        EqProfileType profileType = IsLegendsName(displayName) || IsLegendsName(fullPath)
            ? EqProfileType.EverQuestLegends
            : EqProfileType.EverQuest;
        string name = displayName ?? (profileType == EqProfileType.EverQuestLegends ? "EverQuest Legends" : "EverQuest");
        string id = $"{profileType}:{NormalizePath(fullPath)}";
        return new EqInstall(id, name, profileType, fullPath, source);
    }

    private static bool IsEverQuestName(string? value)
    {
        return value?.Contains("EverQuest", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsLegendsName(string? value)
    {
        return value?.Contains("Legends", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string? FirstExistingInstallPath(params string?[] candidates)
    {
        return candidates.FirstOrDefault(candidate => candidate is not null && File.Exists(Path.Combine(candidate, "eqgame.exe")));
    }

    private static string? TryExtractPathFromCommand(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        string trimmed = command.Trim();
        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1)
            {
                return Path.GetDirectoryName(trimmed[1..endQuote]);
            }
        }

        string firstToken = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? trimmed;
        return Path.GetDirectoryName(firstToken.Trim('"'));
    }
}
