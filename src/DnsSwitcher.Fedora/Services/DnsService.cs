using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;

namespace DnsSwitcher.Fedora.Services;

public sealed class DnsService(ICommandRunner runner, IAdapterService adapters) : IDnsService
{
    private sealed record Values(string Servers, string IgnoreAuto);
    private static string Prefix(int family) => "ipv" + family;
    public Task ApplyAsync(string id, int family, string[] addresses, CancellationToken token)
    {
        var ips = Validation.Addresses(addresses, family, true);
        return ChangeAsync(id, new Dictionary<int, Values> { [family] = new(string.Join(",", ips), "yes") }, token);
    }
    public Task ResetAsync(string id, CancellationToken token) =>
        ChangeAsync(id, new Dictionary<int, Values> { [4] = new("", "no"), [6] = new("", "no") }, token);
    public async Task FlushAsync(CancellationToken token) =>
        await runner.RunAsync("/usr/bin/resolvectl", ["flush-caches"], token).ConfigureAwait(false);

    private async Task<AdapterInfo> Resolve(string id, CancellationToken token)
    {
        var matches = (await adapters.ActiveAsync(token).ConfigureAwait(false)).Where(a => a.Id == id).ToArray();
        return matches.Length switch { 1 => matches[0], 0 => throw new AppError("Disconnected"),
            _ => throw new AppError("SharedProfile") };
    }

    private async Task<Dictionary<int, Values>> Read(string id, IEnumerable<int> families, CancellationToken token)
    {
        var keys = families.SelectMany(f => new[] { Prefix(f) + ".dns", Prefix(f) + ".ignore-auto-dns" });
        var output = await runner.RunAsync(AdapterService.Nmcli,
            ["-t", "-e", "no", "-f", string.Join(",", keys), "connection", "show", "uuid", id], token).ConfigureAwait(false);
        var fields = output.Split('\n').Select(line => line.Split(':', 2)).Where(p => p.Length == 2)
            .ToDictionary(p => p[0], p => p[1].Trim());
        var result = new Dictionary<int, Values>();
        foreach (int f in families)
        {
            var prefix = Prefix(f);
            if (!fields.TryGetValue(prefix + ".dns", out string? dns) ||
                !fields.TryGetValue(prefix + ".ignore-auto-dns", out string? auto) || auto is not ("yes" or "no"))
                throw new AppError("NoFamily");
            result[f] = new(string.Join(",", dns.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)), auto);
        }
        return result;
    }

    private async Task Write(string id, Dictionary<int, Values> settings, CancellationToken token)
    {
        List<string> args = ["--wait", "55", "connection", "modify", "uuid", id];
        foreach (var (f, value) in settings)
            args.AddRange([Prefix(f) + ".dns", value.Servers, Prefix(f) + ".ignore-auto-dns", value.IgnoreAuto]);
        await runner.RunAsync(AdapterService.Nmcli, args.ToArray(), token).ConfigureAwait(false);
    }

    private async Task Reapply(AdapterInfo adapter)
    {
        var current = await Resolve(adapter.Id, CancellationToken.None).ConfigureAwait(false);
        if (current.Name != adapter.Name) throw new AppError("Disconnected");
        await runner.RunAsync(AdapterService.Nmcli, ["--wait", "55", "device", "reapply", current.Name],
            CancellationToken.None).ConfigureAwait(false);
    }

    private async Task ChangeAsync(string id, Dictionary<int, Values> wanted, CancellationToken token)
    {
        var adapter = await Resolve(id, token).ConfigureAwait(false);
        var before = await Read(id, wanted.Keys, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        // Once a profile write starts, finish reapplying it or restoring the previous DNS.
        await Write(id, wanted, CancellationToken.None).ConfigureAwait(false);
        try
        {
            await Reapply(adapter).ConfigureAwait(false);
            var actual = await Read(id, wanted.Keys, CancellationToken.None).ConfigureAwait(false);
            if (wanted.Any(pair => actual[pair.Key] != pair.Value)) throw new AppError("VerifyDns");
            var active = await Resolve(id, CancellationToken.None).ConfigureAwait(false);
            foreach (var (f, value) in wanted.Where(pair => pair.Value.IgnoreAuto == "yes"))
            {
                var live = active.Dns.Where(s => s.Contains(':') == (f == 6)).ToArray();
                if (!live.SequenceEqual(value.Servers.Split(','))) throw new AppError("VerifyDns");
            }
        }
        catch (Exception failure)
        {
            try
            {
                var current = await Read(id, wanted.Keys, CancellationToken.None).ConfigureAwait(false);
                if (wanted.Any(pair => current[pair.Key] != pair.Value)) throw new AppError("VerifyDns");
                await Write(id, before, CancellationToken.None).ConfigureAwait(false);
                await Reapply(adapter).ConfigureAwait(false);
            }
            catch (Exception undo) { throw new AppError("RollbackFailed", $"{failure.Message} / {undo.Message}", failure); }
            throw;
        }
    }
}
