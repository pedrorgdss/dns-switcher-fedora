using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using DnsSwitcher.Core.Models;

namespace DnsSwitcher.Core.Services;

public static partial class Validation
{
    [GeneratedRegex(@"\A[a-z0-9][a-z0-9_-]{0,47}\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    [GeneratedRegex(@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\z", RegexOptions.CultureInvariant)]
    private static partial Regex LabelPattern();

    public static IPAddress Address(string? value, int family)
    {
        string text = value?.Trim() ?? "";
        if (family is not (4 or 6) || !IPAddress.TryParse(text, out var ip))
            throw new AppError("InvalidIp", value);

        // TryParse also accepts legacy shorthand, octal and hexadecimal IPv4 numbers.
        bool valid = family == 4
            ? ip.AddressFamily == AddressFamily.InterNetwork && ip.ToString() == text
            : ip.AddressFamily == AddressFamily.InterNetworkV6 && text.Contains(':') &&
              !text.Contains('[') && !text.Contains(']') && !ip.IsIPv4MappedToIPv6;
        // Use a numeric interface index as an IPv6 scope identifier.
        int scope = text.IndexOf('%');
        if (scope >= 0 && (scope == text.Length - 1 || !text[(scope + 1)..].All(char.IsAsciiDigit)))
            throw new AppError("InvalidIp", value);
        if (!valid || ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) ||
            ip.Equals(IPAddress.Broadcast) || ip.IsIPv6Multicast ||
            (family == 4 && ip.GetAddressBytes()[0] is >= 224 and <= 239))
            throw new AppError("InvalidIp", value);
        return ip;
    }

    public static string[] Addresses(string[]? values, int family, bool required = false)
    {
        if (values == null || values.Length > 2 || (required && values.Length == 0))
            throw new AppError("AddressCount");
        var result = values.Select(x => Address(x, family).ToString()).ToArray();
        if (result.Distinct().Count() != result.Length) throw new AppError("DuplicateIp");
        return result;
    }

    public static string[] Domains(string[]? domains)
    {
        if (domains is null || domains.Length is < 1 or > 32) throw new AppError("InvalidDomain");
        return domains.Select(value =>
        {
            var name = (value ?? "").Trim().TrimEndOneDot().ToLowerInvariant();
            if (name.Length is < 1 or > 253 || name.Split('.').Any(label => !LabelPattern().IsMatch(label)))
                throw new AppError("InvalidDomain", value);
            return name;
        }).Distinct().ToArray();
    }

    private static string TrimEndOneDot(this string value) =>
        value.EndsWith('.') ? value[..^1] : value;

    public static Provider[] Providers(Provider[]? providers)
    {
        if (providers is null || providers.Length > 64) throw new AppError("InvalidProviders");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return providers.Select(p =>
        {
            if (p == null || string.IsNullOrEmpty(p.Id) || !IdPattern().IsMatch(p.Id) ||
                p.Id == "custom" || !ids.Add(p.Id) || string.IsNullOrWhiteSpace(p.Name) ||
                p.Name.Length > 64 || p.Name.Any(char.IsControl)) throw new AppError("InvalidProvider");
            var v4 = Addresses(p.Ipv4, 4);
            var v6 = Addresses(p.Ipv6, 6);
            if (v4.Length + v6.Length == 0) throw new AppError("AddressCount");
            return p with { Name = p.Name.Trim(), Ipv4 = v4, Ipv6 = v6 };
        }).ToArray();
    }

    public static AppConfig Config(AppConfig? config)
    {
        if (config == null || config.Version != 1 || config.Family is not (4 or 6) ||
            config.Rounds is < 1 or > 20 ||
            (config.AdapterId != null && !Guid.TryParse(config.AdapterId, out _)))
            throw new AppError("InvalidConfig");
        var providers = Providers(config.Providers);
        return config with {
            Providers = providers, Domains = Domains(config.Domains),
            CustomV4 = Addresses(config.CustomV4, 4), CustomV6 = Addresses(config.CustomV6, 6),
            ProviderId = config.ProviderId == "custom" || providers.Any(p => p.Id == config.ProviderId)
                ? config.ProviderId : providers.FirstOrDefault()?.Id ?? "custom"
        };
    }
}
