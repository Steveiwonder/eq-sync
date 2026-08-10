using EqSync.Core;

namespace EqSync.Core.Tests;

public sealed class LanPeerDiscoveryTests
{
    [Fact]
    public void IsCompatible_RejectsDifferentAppVersions()
    {
        PeerInfo local = Peer("local", "0.1.3");
        PeerInfo remote = Peer("remote", "0.1.2");

        Assert.False(LanPeerDiscovery.IsCompatible(local, remote));
    }

    [Fact]
    public void IsCompatible_AcceptsSameAppVersionDifferentMachine()
    {
        PeerInfo local = Peer("local", "0.1.3");
        PeerInfo remote = Peer("remote", "0.1.3");

        Assert.True(LanPeerDiscovery.IsCompatible(local, remote));
    }

    [Fact]
    public void IsCompatible_RejectsSelf()
    {
        PeerInfo local = Peer("local", "0.1.3");
        PeerInfo remote = Peer("local", "0.1.3");

        Assert.False(LanPeerDiscovery.IsCompatible(local, remote));
    }

    private static PeerInfo Peer(string machineId, string version)
    {
        return new PeerInfo(machineId, machineId, version, new Uri("http://localhost/"), true, [EqProfileType.EverQuest]);
    }
}
