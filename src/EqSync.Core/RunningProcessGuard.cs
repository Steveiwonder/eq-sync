using System.Diagnostics;

namespace EqSync.Core;

public interface IRunningProcessGuard
{
    bool IsSyncBlocked();

    IReadOnlyList<string> GetBlockingProcesses();
}

public sealed class RunningProcessGuard : IRunningProcessGuard
{
    private static readonly string[] BlockingProcessNames =
    [
        "eqgame",
        "LaunchPad",
        "LaunchPadShell",
        "AwesomiumProcess"
    ];

    public bool IsSyncBlocked()
    {
        return GetBlockingProcesses().Count > 0;
    }

    public IReadOnlyList<string> GetBlockingProcesses()
    {
        return Process.GetProcesses()
            .Where(process => BlockingProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase))
            .Select(process => process.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
