using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;

namespace DnsSwitcher.Fedora.Services;

public sealed class AdapterService(ICommandRunner runner) : IAdapterService
{
    internal const string Nmcli = "/usr/bin/nmcli";
    private const string Fields = "GENERAL.DEVICE,GENERAL.TYPE,GENERAL.STATE,GENERAL.CONNECTION,GENERAL.CON-UUID,IP4.ADDRESS,IP4.DNS,IP6.ADDRESS,IP6.DNS";

    public async Task<AdapterInfo[]> ActiveAsync(CancellationToken token) =>
        Parse(await runner.RunAsync(Nmcli, ["-t", "-e", "no", "-f", Fields, "device", "show"], token).ConfigureAwait(false));

    public static AdapterInfo[] Parse(string output)
    {
        var result = new List<AdapterInfo>();
        foreach (var section in output.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries))
        {
            // Split at the first colon only: IPv6 addresses and profile names can contain colons.
            var fields = section.Split('\n').Select(line => line.Split(':', 2)).Where(parts => parts.Length == 2)
                .ToLookup(parts => parts[0].Split('[')[0], parts => parts[1], StringComparer.Ordinal);
            string? Get(string key) => fields[key].FirstOrDefault();
            var device = Get("GENERAL.DEVICE");
            if (string.IsNullOrEmpty(device) || Get("GENERAL.TYPE") == "loopback" ||
                Get("GENERAL.STATE")?.Split(' ')[0] != "100" || !Guid.TryParse(Get("GENERAL.CON-UUID"), out var uuid)) continue;
            result.Add(new AdapterInfo(uuid.ToString(), device, Get("GENERAL.CONNECTION") ?? device,
                fields["IP4.ADDRESS"].Select(s => s.Split('/')[0]).ToArray(),
                fields["IP6.ADDRESS"].Select(s => s.Split('/')[0]).ToArray(),
                fields["IP4.DNS"].Concat(fields["IP6.DNS"]).Where(s => s.Length > 0).ToArray()));
        }
        return result.OrderBy(a => a.Name, StringComparer.Ordinal).ToArray();
    }
}
