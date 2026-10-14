using System.Net;
using RetakeV4.Domain.Stats;

namespace RetakeV4.Domain.Tests.Stats;

public class CountryResolverTests
{
    private sealed class FakeLookup : ICountryLookup
    {
        private readonly Dictionary<string, string?> _countries = new()
        {
            ["81.2.69.160"] = "fr",
            ["2a01:e0a::1"] = "DE",
            ["1.1.1.1"] = "EUR",
        };

        public string? Country(IPAddress address) => _countries.GetValueOrDefault(address.ToString());
    }

    private sealed class ThrowingLookup : ICountryLookup
    {
        public string? Country(IPAddress address) => throw new ObjectDisposedException("reader");
    }

    [Theory]
    [InlineData("81.2.69.160:27005", "FR")]
    [InlineData("81.2.69.160", "FR")]
    [InlineData("[::ffff:81.2.69.160]:27005", "FR")]
    [InlineData("[2a01:e0a::1]:27005", "DE")]
    [InlineData("8.8.8.8:1", null)]
    [InlineData("1.1.1.1:1", null)]
    [InlineData("10.0.0.5:27005", null)]
    [InlineData("192.168.1.2:1", null)]
    [InlineData("172.16.0.1:1", null)]
    [InlineData("127.0.0.1:27005", null)]
    [InlineData("100.64.0.1:1", null)]
    [InlineData("169.254.1.1:1", null)]
    [InlineData("[::1]:27005", null)]
    [InlineData("[fe80::1]:1", null)]
    [InlineData("[fd00::1]:1", null)]
    [InlineData("loopback", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Resolve_PublicAddressesOnly(string? raw, string? expected) => Assert.Equal(expected, CountryResolver.Resolve(new FakeLookup(), raw));

    [Fact]
    public void FailingLookup_GivesNoCountry() => Assert.Null(CountryResolver.Resolve(new ThrowingLookup(), "81.2.69.160:27005"));

    [Theory]
    [InlineData("FR", true)]
    [InlineData("fr", true)]
    [InlineData("F1", false)]
    [InlineData("FRA", false)]
    [InlineData(null, false)]
    public void IsIsoCode(string? code, bool expected) => Assert.Equal(expected, CountryResolver.IsIsoCode(code));
}
