using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace EqSync.Core;

public sealed class LanPeerDiscovery : IPeerDiscovery
{
    public const int DiscoveryPort = 47641;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Func<PeerInfo> _localPeerFactory;
    private readonly ConcurrentDictionary<string, PeerInfo> _peers = new(StringComparer.OrdinalIgnoreCase);

    public LanPeerDiscovery(Func<PeerInfo> localPeerFactory)
    {
        _localPeerFactory = localPeerFactory;
    }

    public async IAsyncEnumerable<PeerInfo> DiscoverAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using UdpClient listener = new(DiscoveryPort)
        {
            EnableBroadcast = true
        };
        using UdpClient broadcaster = new()
        {
            EnableBroadcast = true
        };

        Task broadcastTask = BroadcastLoopAsync(broadcaster, cancellationToken);

        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await listener.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            PeerInfo? peer = TryParsePeer(result.Buffer);
            if (peer is null || peer.MachineId == _localPeerFactory().MachineId)
            {
                continue;
            }

            if (_peers.TryAdd(peer.MachineId, peer))
            {
                yield return peer;
            }
        }

        await WaitQuietlyAsync(broadcastTask);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }

    private async Task BroadcastLoopAsync(UdpClient broadcaster, CancellationToken cancellationToken)
    {
        IPEndPoint endpoint = new(IPAddress.Broadcast, DiscoveryPort);
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] payload = JsonSerializer.SerializeToUtf8Bytes(_localPeerFactory(), JsonOptions);
            await broadcaster.SendAsync(payload, endpoint, cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private static PeerInfo? TryParsePeer(byte[] buffer)
    {
        try
        {
            string json = Encoding.UTF8.GetString(buffer);
            return JsonSerializer.Deserialize<PeerInfo>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static async Task WaitQuietlyAsync(Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
    }
}
