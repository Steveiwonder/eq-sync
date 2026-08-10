namespace EqSync.Core;

public enum SyncTransportKind
{
    LocalNetwork,
    RemoteServer
}

public interface IPeerDiscovery : IAsyncDisposable
{
    IAsyncEnumerable<PeerInfo> DiscoverAsync(CancellationToken cancellationToken);
}

public interface IPeerTransport
{
    Task<SyncManifest> GetManifestAsync(PeerInfo peer, EqProfileType profileType, CancellationToken cancellationToken);
}

public interface IPairingService
{
    string CreatePin();

    bool VerifyPin(string expectedPin, string providedPin);
}

public sealed class PinPairingService : IPairingService
{
    public string CreatePin()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }

    public bool VerifyPin(string expectedPin, string providedPin)
    {
        return string.Equals(expectedPin, providedPin, StringComparison.Ordinal);
    }
}
