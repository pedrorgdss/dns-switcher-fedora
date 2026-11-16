using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using Xunit;

namespace DnsSwitcher.Tests;

public class ConfigTests
{
    private const string Minimal = """
      {"providers":[{"id":"local","name":"Local","ipv4":["127.0.0.1"],"ipv6":[]}]}
      """;

    [Fact]
    public void ParsesProviderAndFillsOptionalSettings()
    {
        var c = ConfigService.Parse(Minimal);
        Assert.Equal("local", c.ProviderId);
        Assert.Equal(3, c.Rounds); Assert.Equal(5, c.Domains.Length);
        Assert.Equal("127.0.0.1", Assert.Single(Assert.Single(c.Providers).Ipv4));
    }

    [Fact]
    public void LoadsEmbeddedDefaults()
    {
        var config = ConfigService.Defaults();
        Assert.Equal(6, config.Providers.Length);
        Assert.All(config.Providers, p => { Assert.Equal(2, p.Ipv4.Length); Assert.Equal(2, p.Ipv6.Length); });
        Assert.Equal(360, BenchmarkService.QueryCount(config));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("""{"providers":null}""")]
    [InlineData("""{"providers":[],"rounds":0}""")]
    [InlineData("""{"providers":[],"domains":[]}""")]
    [InlineData("""{"providers":[],"version":2}""")]
    [InlineData("""{"providers":[],"adapterId":"Wi-Fi"}""")]
    [InlineData("""{"providers":[],"extraneous":123}""")]
    [InlineData("""{"providers":[null]}""")]
    [InlineData("""{"providers":[{"id":"custom","name":"x","ipv4":[],"ipv6":[]}]}""")]
    public void RefusesInvalidConfig(string input) =>
        Assert.Throws<AppError>(() => ConfigService.Parse(input));

    [Fact]
    public void RejectsDuplicateIds()
    {
        var provider = Assert.Single(ConfigService.Parse(Minimal).Providers);
        Assert.Throws<AppError>(() => Validation.Providers([provider, provider]));
    }

    [Fact]
    public async Task PreservesOriginalFileOnFailureAndRoundTripsProviders()
    {
        string dir = Directory.CreateTempSubdirectory("dns-switcher-").FullName;
        try
        {
            var store = new ConfigService(dir);
            var first = await store.LoadAsync(default);
            await store.UpdateAsync(c => c with { Rounds = 7, ProviderId = "quad9" }, default);
            var second = await store.LoadAsync(default);
            Assert.Equal(7, second.Rounds); Assert.Equal("quad9", second.ProviderId);
            string path = Path.Combine(dir, "providers.json");
            await store.ExportAsync(path, first.Providers, default);
            var imported = await store.ImportAsync(path, default);
            Assert.Equal(first.Providers.Select(p => p.Id), imported.Select(p => p.Id));
            var settings = Path.Combine(dir, "dnsswitcher.json");
            await File.WriteAllTextAsync(settings, "broken");
            await Assert.ThrowsAsync<AppError>(() => store.UpdateAsync(c => c with { Rounds = 1 }, default));
            Assert.Equal("broken", await File.ReadAllTextAsync(settings));
            Assert.Empty(Directory.GetFiles(dir, "*.tmp"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
