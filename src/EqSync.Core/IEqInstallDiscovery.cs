namespace EqSync.Core;

public interface IEqInstallDiscovery
{
    IReadOnlyList<EqInstall> Discover();
}
