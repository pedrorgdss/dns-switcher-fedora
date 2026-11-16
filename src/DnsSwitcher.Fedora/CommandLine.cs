using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using static DnsSwitcher.Fedora.Resources.Strings;

namespace DnsSwitcher.Fedora;

internal static class CommandLine
{
    public static async Task<int> RunAsync(string[] args, IConfigService configs, IAdapterService adapters, IDnsService dns)
    {
        if (args is ["--help"] or ["-h"]) { Console.WriteLine(T("Usage")); return 0; }
        if (args is ["--list"])
        {
            foreach (var a in await adapters.ActiveAsync(default))
                Console.WriteLine($"{a.Name}  {a.Profile}  {a.Id}  IPv4 {string.Join(", ", a.Ipv4)}  DNS {string.Join(", ", a.Dns)}");
            return 0;
        }
        string? action = null, providerId = null, name = null;
        int? family = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--apply" when action == null:
                    action = "apply"; providerId = Next(args, ref i); break;
                case "--reset" or "--flush" when action == null:
                    action = args[i][2..]; break;
                case "--adapter" when name == null:
                    name = Next(args, ref i); break;
                case "--family" when family == null:
                    family = Next(args, ref i) switch { "4" => 4, "6" => 6, _ => throw new AppError("Usage") };
                    break;
                default: throw new AppError("Usage");
            }
        }
        if (action == null || (action != "apply" && family != null) || (action == "flush" && name != null))
            throw new AppError("Usage");
        var token = CancellationToken.None;
        if (action == "flush") { await dns.FlushAsync(token); Console.WriteLine(T("Flushed")); return 0; }
        var config = await configs.LoadAsync(token);
        var active = await adapters.ActiveAsync(token);
        var adapter = name != null
            ? active.FirstOrDefault(a => a.Name.Equals(name, StringComparison.Ordinal) ||
                (Guid.TryParse(name, out var id) && Guid.Parse(a.Id) == id))
            : active.FirstOrDefault(a => a.Id == config.AdapterId) ??
                (config.AdapterId == null && active.Length == 1 ? active[0] : null);
        if (adapter == null) throw new AppError(name != null || config.AdapterId != null ? "Disconnected" : "ChooseAdapter");
        if (action == "reset")
        {
            await dns.ResetAsync(adapter.Id, token);
            Console.WriteLine(T("ResetDone"));
            await configs.UpdateAsync(c => c with { AdapterId = adapter.Id }, token);
        }
        else
        {
            var p = config.Providers.FirstOrDefault(p => p.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase))
                ?? throw new AppError("UnknownProvider", providerId);
            int version = family ?? 4;
            await dns.ApplyAsync(adapter.Id, version, p.Servers(version), token);
            Console.WriteLine(T("Applied"));
            await configs.UpdateAsync(c => c with { AdapterId = adapter.Id, ProviderId = p.Id, Family = version, PreferAlternate = false }, token);
        }
        return 0;
    }

    private static string Next(string[] args, ref int i)
    {
        if (++i >= args.Length || string.IsNullOrWhiteSpace(args[i]) || args[i].StartsWith("--", StringComparison.Ordinal))
            throw new AppError("Usage");
        return args[i];
    }
}
