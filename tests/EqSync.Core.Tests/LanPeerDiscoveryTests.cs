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

    [Fact]
    public void UseObservedEndpointAddress_ReplacesAdvertisedHostWithPacketSourceAddress()
    {
        PeerInfo remote = new("remote", "Remote", "0.1.3", new Uri("http://remote-host:47642/"), true, [EqProfileType.EverQuest]);

        PeerInfo observed = LanPeerDiscovery.UseObservedEndpointAddress(remote, new System.Net.IPEndPoint(System.Net.IPAddress.Parse("192.168.1.50"), 54321));

        Assert.Equal("http://192.168.1.50:47642/", observed.Endpoint.ToString());
    }

    private static PeerInfo Peer(string machineId, string version)
    {
        return new PeerInfo(machineId, machineId, version, new Uri("http://localhost/"), true, [EqProfileType.EverQuest]);
    }
}
