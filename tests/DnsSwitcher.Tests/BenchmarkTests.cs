using System.Net;
using System.Net.Sockets;
using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using Xunit;

namespace DnsSwitcher.Tests;

public class BenchmarkTests
{
    private static readonly AdapterInfo Adapter = new(Guid.Empty.ToString(), "lo", "Test", ["127.0.0.1"], ["::1"], []);

    [Fact]
    public async Task AggregatesSuccessfulQueriesAndRanksTotalFailuresLast()
    {
        var c = ConfigService.Defaults() with {
            Rounds = 1, Domains = ["a.test", "b.test", "c.test"],
            Providers = [new Provider("one", "One", ["1.1.1.1", "1.0.0.1"], ["::1"])]
        };
        var service = new BenchmarkService((ip, _, domain, _) => Task.FromResult<double?>(ip.ToString() switch {
            "1.1.1.1" => domain switch { "a.test" => 10, "b.test" => 20, _ => null },
            "1.0.0.1" => 25, _ => null
        }));
        var rows = await service.RunAsync(c, Adapter, null, default);
        Assert.Equal(3, rows.Length);
        var best = rows[0];
        Assert.Equal("1.1.1.1", best.Server); Assert.Equal(15, best.Mean);
        Assert.Equal(10, best.Minimum); Assert.Equal(20, best.Maximum); Assert.Equal(1.0/3, best.FailureRate);
        Assert.Null(rows[^1].Mean); Assert.Equal(1, rows[^1].FailureRate);
    }

    [Fact]
    public async Task CancelsRunningQueries()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new BenchmarkService(async (_, _, _, token) =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return null;
        });
        using var cancel = new CancellationTokenSource();
        var run = service.RunAsync(ConfigService.Defaults(), Adapter, null, cancel.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public async Task ResolvesAnActualUdpQueryOnLoopback()
    {
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        var sample = BenchmarkService.MeasureAsync(IPAddress.Loopback, port, "local.test", timer.Token, "lo");
        var request = await server.ReceiveAsync(timer.Token);
        var response = Answer(request.Buffer);
        await server.SendAsync(response, request.RemoteEndPoint, timer.Token);
        Assert.NotNull(await sample);
    }

    [Fact]
    public async Task SilentUdpPeerTimesOut()
    {
        using var server = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)server.Client.LocalEndPoint!).Port;
        using var timer = new CancellationTokenSource(TimeSpan.FromSeconds(6));
        Assert.Null(await BenchmarkService.MeasureAsync(IPAddress.Loopback, port, "local.test", timer.Token));
    }

    [Fact]
    public void RejectsMismatchedTruncatedOrNegativeReplies()
    {
        var query = DnsPacket.Query("local.test");
        var packet = Answer(query);
        Assert.True(DnsPacket.HasAnswer(packet, query, "local.test"));
        Assert.False(DnsPacket.HasAnswer(packet.AsSpan(0, packet.Length-1), query, "local.test"));
        Assert.False(DnsPacket.HasAnswer(packet, query, "unrelated.test"));
        packet[0] ^= 1;
        Assert.False(DnsPacket.HasAnswer(packet, query, "local.test"));
        packet[0] ^= 1; packet[3] |= 3;
        Assert.False(DnsPacket.HasAnswer(packet, query, "local.test"));
    }

    private static byte[] Answer(byte[] query)
    {
        byte[] packet = [.. query, 0xc0, 12, 0, 1, 0, 1, 0, 0, 0, 30, 0, 4, 127, 0, 0, 1];
        packet[2] = 0x81; packet[3] = 0x80; packet[7] = 1;
        return packet;
    }
}
