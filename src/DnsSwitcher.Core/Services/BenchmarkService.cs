using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using DnsSwitcher.Core.Models;

namespace DnsSwitcher.Core.Services;

public sealed class BenchmarkService : IBenchmarkService
{
    private readonly Func<IPAddress, string, string, CancellationToken, Task<double?>> measure;

    public BenchmarkService() : this((ip, device, domain, token) => MeasureAsync(ip, 53, domain, token, device)) { }

    internal BenchmarkService(Func<IPAddress, string, string, CancellationToken, Task<double?>> measure)
    {
        this.measure = measure;
    }

    public static int QueryCount(AppConfig config) =>
        config.Providers.Sum(p => p.Ipv4.Length + p.Ipv6.Length) * config.Domains.Length * config.Rounds;

    public async Task<BenchmarkResult[]> RunAsync(AppConfig config, AdapterInfo adapter, IProgress<int>? completed, CancellationToken token)
    {
        config = Validation.Config(config);
        var results = new ConcurrentBag<BenchmarkResult>();
        int count = 0;
        var targets = config.Providers.SelectMany(p => p.Ipv4.Concat(p.Ipv6).Select(ip => (p, ip)));
        var options = new ParallelOptions { MaxDegreeOfParallelism = 8, CancellationToken = token };
        await Parallel.ForEachAsync(targets, options, async (target, stop) =>
        {
            var samples = new List<double>();
            int failed = 0;
            var address = IPAddress.Parse(target.ip);
            bool available = (address.AddressFamily == AddressFamily.InterNetwork ? adapter.Ipv4 : adapter.Ipv6).Length > 0;
            for (int round = 0; round < config.Rounds; round++)
            {
                foreach (string domain in config.Domains)
                {
                    stop.ThrowIfCancellationRequested();
                    var elapsed = available ? await measure(address, adapter.Name, domain, stop).ConfigureAwait(false) : null;
                    if (elapsed.HasValue) samples.Add(elapsed.Value);
                    else failed++;
                    completed?.Report(Interlocked.Increment(ref count));
                }
            }
            results.Add(new BenchmarkResult(target.p, target.ip, failed + samples.Count, failed,
                samples.Count > 0 ? samples.Average() : null, samples.Count > 0 ? samples.Min() : null,
                samples.Count > 0 ? samples.Max() : null));
        }).ConfigureAwait(false);
        return results.OrderBy(r => r.Mean ?? double.PositiveInfinity).ThenBy(r => r.FailureRate)
            .ThenBy(r => r.Provider.Id, StringComparer.Ordinal).ThenBy(r => r.Server, StringComparer.Ordinal).ToArray();
    }

    internal static async Task<double?> MeasureAsync(IPAddress ip, int port, string domain, CancellationToken stop, string? device = null)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var udp = new UdpClient(ip.AddressFamily);
            if (device != null)
            {
                // Linux SOL_SOCKET/SO_BINDTODEVICE binds the first outgoing interface.
                udp.Client.SetRawSocketOption(1, 25, System.Text.Encoding.UTF8.GetBytes(device + "\0"));
            }
            udp.Connect(ip, port); // A connected UDP socket only receives this peer's datagrams.
            byte[] query = DnsPacket.Query(domain);
            var start = Stopwatch.GetTimestamp();
            await udp.SendAsync(query, deadline.Token).ConfigureAwait(false);
            var response = await udp.ReceiveAsync(deadline.Token).ConfigureAwait(false);
            var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return DnsPacket.HasAnswer(response.Buffer, query, domain) ? elapsed : null;
        }
        catch (OperationCanceledException) when (!stop.IsCancellationRequested) { return null; }
        catch (SocketException) { stop.ThrowIfCancellationRequested(); return null; }
        catch (PlatformNotSupportedException) { return null; }
    }
}
