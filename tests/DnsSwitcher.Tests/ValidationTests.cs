using DnsSwitcher.Core.Models;
using DnsSwitcher.Core.Services;
using Xunit;

namespace DnsSwitcher.Tests;

public class ValidationTests
{
    [Theory]
    [InlineData("8.8.8.8", 4, "8.8.8.8")]
    [InlineData(" 1.0.0.1 ", 4, "1.0.0.1")]
    [InlineData("2001:4860:4860:0:0:0:0:8888", 6, "2001:4860:4860::8888")]
    [InlineData("fe80::1%3", 6, "fe80::1%3")]
    [InlineData("::1", 6, "::1")]
    public void CanonicalizesValidAddress(string input, int family, string expected) =>
        Assert.Equal(expected, Validation.Address(input, family).ToString());

    [Theory]
    [InlineData("1.2.3", 4)]
    [InlineData("010.0.0.1", 4)]
    [InlineData("0x08080808", 4)]
    [InlineData("4294967295", 4)]
    [InlineData("8.8.8.8;exit", 4)]
    [InlineData("256.1.1.1", 4)]
    [InlineData("0.0.0.0", 4)]
    [InlineData("224.0.0.1", 4)]
    [InlineData("::", 6)]
    [InlineData("ff02::1", 6)]
    [InlineData("[2606:4700::1111]:53", 6)]
    [InlineData("8.8.8.8", 6)]
    [InlineData("fe80::1%noop", 6)]
    [InlineData("::ffff:1.1.1.1", 6)]
    public void RejectsInvalidEndpoint(string input, int family) =>
        Assert.Throws<AppError>(() => Validation.Address(input, family));

    [Fact]
    public void DetectsDuplicateCanonicalIpv6Addresses() =>
        Assert.Throws<AppError>(() => Validation.Addresses(["2606::1", "2606:0:0:0:0:0:0:1"], 6));

    [Theory]
    [InlineData("a..b")]
    [InlineData("-dns.example")]
    [InlineData("domain..")]
    [InlineData("one/two.com")]
    public void RejectsMalformedDomains(string value) =>
        Assert.Throws<AppError>(() => Validation.Domains([value]));
}
