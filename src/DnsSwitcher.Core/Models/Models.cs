namespace DnsSwitcher.Core.Models;

public sealed record Provider(string Id, string Name, string[] Ipv4, string[] Ipv6)
{
    public string[] Servers(int family) => family == 4 ? Ipv4 : Ipv6;
    public override string ToString() => Name;
}

public sealed record AppConfig
{
    public int Version { get; init; } = 1;
    public required Provider[] Providers { get; init; }
    public string[] Domains { get; init; } = ["google.com", "youtube.com", "wikipedia.org", "amazon.com", "github.com"];
    public int Rounds { get; init; } = 3;
    public string? AdapterId { get; init; }
    public string ProviderId { get; init; } = "cloudflare";
    public int Family { get; init; } = 4;
    public bool PreferAlternate { get; init; }
    public string[] CustomV4 { get; init; } = [];
    public string[] CustomV6 { get; init; } = [];
}

public sealed record AdapterInfo(string Id, string Name, string Profile, string[] Ipv4, string[] Ipv6, string[] Dns)
{
    public override string ToString() => Name;
}

public sealed record BenchmarkResult(Provider Provider, string Server, int Total, int Failed,
    double? Mean, double? Minimum, double? Maximum)
{
    public double FailureRate => (double)Failed / Total;
}

// Resource keys cross the boundary between the portable services and the localized UI.
public sealed class AppError(string key, string? detail = null, Exception? inner = null)
    : Exception(key, inner)
{
    public string Key { get; } = key;
    public string? Detail { get; } = detail;
}
