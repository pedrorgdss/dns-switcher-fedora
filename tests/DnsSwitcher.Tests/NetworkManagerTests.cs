using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using DnsSwitcher.Fedora.Services;
using Xunit;

namespace DnsSwitcher.Tests;

public class NetworkManagerTests
{
    private const string Id = "f82cbcd4-5fd3-4e03-9f80-0c13072da575";

    [Fact]
    public void ReadsActiveProfileAndBothFamilies()
    {
        string output = $"""
            GENERAL.DEVICE:wlp1s0
            GENERAL.TYPE:wifi
            GENERAL.STATE:100 (connected)
            GENERAL.CONNECTION:Café: rede de teste
            GENERAL.CON-UUID:{Id}
            IP4.ADDRESS[1]:192.0.2.2/24
            IP4.DNS[1]:192.0.2.1
            IP6.ADDRESS[1]:2001:db8::22/64
            IP6.DNS[1]:2001:db8::53

            GENERAL.DEVICE:lo
            GENERAL.TYPE:loopback
            GENERAL.STATE:100 (connected)
            GENERAL.CON-UUID:{Id}

            GENERAL.DEVICE:enp2s0
            GENERAL.TYPE:ethernet
            GENERAL.STATE:30 (disconnected)
            """;
        var a = Assert.Single(AdapterService.Parse(output));
        Assert.Equal("wlp1s0", a.Name); Assert.Equal("Café: rede de teste", a.Profile);
        Assert.Equal(["192.0.2.1", "2001:db8::53"], a.Dns);
        Assert.Equal("192.0.2.2", Assert.Single(a.Ipv4));
    }

    [Fact]
    public async Task ApplyWritesSelectedFamilyAndResetRestoresBoth()
    {
        var nm = new Network(); var service = new DnsService(nm, nm);
        await service.ApplyAsync(Id, 4, ["8.8.8.8","8.8.4.4"], default);
        Assert.Equal("8.8.8.8,8.8.4.4", nm.Profile["ipv4.dns"]);
        Assert.Equal("yes", nm.Profile["ipv4.ignore-auto-dns"]);
        Assert.Equal("::1", nm.Profile["ipv6.dns"]);
        await service.ApplyAsync(Id, 6, ["2606:4700:4700::1111"], default);
        await service.ResetAsync(Id, default);
        foreach (var family in new[] {4,6})
        {
            Assert.Equal("", nm.Profile[$"ipv{family}.dns"]);
            Assert.Equal("no", nm.Profile[$"ipv{family}.ignore-auto-dns"]);
        }
        Assert.Equal(3, nm.Reapplied);
    }

    [Fact]
    public async Task ReapplyFailureRestoresPreviousProfile()
    {
        var nm = new Network { FailNextReapply = true };
        var service = new DnsService(nm, nm);
        await Assert.ThrowsAsync<AppError>(() => service.ApplyAsync(Id, 4, ["1.1.1.1"], default));
        Assert.Equal("127.0.0.1", nm.Profile["ipv4.dns"]); Assert.Equal("::1", nm.Profile["ipv6.dns"]);
        Assert.Equal(2, nm.Reapplied);
    }

    [Fact]
    public async Task DisconnectedAdapterIsNeverModified()
    {
        var nm = new Network(); var service = new DnsService(nm, nm);
        var error = await Assert.ThrowsAsync<AppError>(() => service.ResetAsync(Guid.Empty.ToString(), default));
        Assert.Equal("Disconnected", error.Key); Assert.Equal(0, nm.Reapplied);
    }

    [Fact]
    public async Task FlushUsesResolved()
    {
        var nm = new Network(); await new DnsService(nm, nm).FlushAsync(default);
        Assert.True(nm.Flushed);
    }

    private sealed class Network : ICommandRunner, IAdapterService
    {
        public readonly Dictionary<string, string> Profile = new() {
            ["ipv4.dns"]="127.0.0.1", ["ipv4.ignore-auto-dns"]="yes",
            ["ipv6.dns"]="::1", ["ipv6.ignore-auto-dns"]="yes" };
        public bool FailNextReapply;
        public int Reapplied;
        public bool Flushed;
        public Task<AdapterInfo[]> ActiveAsync(CancellationToken token) => Task.FromResult<AdapterInfo[]>([
            new(Id, "enp1s0", "Test", ["192.0.2.2"], ["2001:db8::2"],
                (Profile["ipv4.dns"] + "," + Profile["ipv6.dns"]).Split(',',StringSplitOptions.RemoveEmptyEntries))]);

        public Task<string> RunAsync(string program, string[] args, CancellationToken token)
        {
            if (program == "/usr/bin/resolvectl")
            {
                Assert.Equal(["flush-caches"], args); Flushed = true;
            }
            else if (args.Contains("show"))
            {
                Assert.Equal(Id, args[^1]);
                return Task.FromResult(string.Join('\n', Profile.Select(kv => kv.Key + ":" + kv.Value)));
            }
            else if (args.Contains("modify"))
            {
                int first = Array.IndexOf(args, "uuid") + 1; Assert.Equal(Id, args[first]);
                for (int i = first + 1; i < args.Length; i += 2) Profile[args[i]] = args[i+1];
            }
            else
            {
                Assert.Equal("reapply", args[^2]); Assert.Equal("enp1s0", args[^1]);
                Reapplied++;
                if (FailNextReapply) { FailNextReapply = false; throw new AppError("SystemError"); }
            }
            return Task.FromResult("");
        }
    }
}
