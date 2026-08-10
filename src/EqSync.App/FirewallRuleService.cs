using System.Diagnostics;

namespace EqSync.App;

internal sealed class FirewallRuleService
{
    private const string TcpRuleName = "EQ Sync TCP 47642";
    private const string UdpRuleName = "EQ Sync UDP 47641";

    public bool AreRulesPresent()
    {
        return RuleExists(TcpRuleName) && RuleExists(UdpRuleName);
    }

    public void LaunchElevatedRuleInstaller()
    {
        string command = string.Join("; ",
        [
            $"if (-not (Get-NetFirewallRule -DisplayName '{TcpRuleName}' -ErrorAction SilentlyContinue)) {{ New-NetFirewallRule -DisplayName '{TcpRuleName}' -Direction Inbound -Action Allow -Protocol TCP -LocalPort 47642 -RemoteAddress LocalSubnet -Profile Any | Out-Null }}",
            $"if (-not (Get-NetFirewallRule -DisplayName '{UdpRuleName}' -ErrorAction SilentlyContinue)) {{ New-NetFirewallRule -DisplayName '{UdpRuleName}' -Direction Inbound -Action Allow -Protocol UDP -LocalPort 47641 -RemoteAddress LocalSubnet -Profile Any | Out-Null }}"
        ]);

        Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
            UseShellExecute = true,
            Verb = "runas"
        });
    }

    private static bool RuleExists(string ruleName)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -Command \"if (Get-NetFirewallRule -DisplayName '{ruleName}' -ErrorAction SilentlyContinue) {{ exit 0 }} else {{ exit 1 }}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            })!;
            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
