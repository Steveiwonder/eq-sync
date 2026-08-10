using System.Reflection;

namespace EqSync.App;

internal static class AppVersionProvider
{
    public static string Current =>
        typeof(AppVersionProvider).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ??
        typeof(AppVersionProvider).Assembly.GetName().Version?.ToString() ??
        "0.0.0";
}
