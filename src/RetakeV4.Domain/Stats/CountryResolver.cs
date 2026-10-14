using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;

namespace RetakeV4.Domain.Stats;

// Country database (DB-IP Lite in game). Thread-safe; it may throw once disposed.
public interface ICountryLookup
{
    string? Country(IPAddress address);
}

// Turns the address CS2 reports for a player into an ISO 3166-1 alpha-2 code. The address is only handed to the
// lookup: it is never returned, stored or logged.
public static class CountryResolver
{
    public static string? Resolve(ICountryLookup lookup, string? rawAddress)
    {
        ArgumentNullException.ThrowIfNull(lookup);
        if (ParsePublic(rawAddress) is not { } address)
        {
            return null;
        }
        string? code;
        try
        {
            code = lookup.Country(address);
        }
        catch (Exception)
        {
            // A failing or disposed reader only means "unknown country"; nothing is logged (the address must not leak).
            return null;
        }
        return IsIsoCode(code) ? code.ToUpperInvariant() : null;
    }

    // "a.b.c.d:port", "[v6]:port" or a bare address. Private, loopback, link-local and CGNAT addresses have no country.
    public static IPAddress? ParsePublic(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !IPEndPoint.TryParse(raw.Trim(), out var endpoint))
        {
            return null;
        }
        var address = endpoint.Address.IsIPv4MappedToIPv6 ? endpoint.Address.MapToIPv4() : endpoint.Address;
        return IsPublic(address) ? address : null;
    }

    public static bool IsIsoCode([NotNullWhen(true)] string? code) =>
        code is { Length: 2 } && char.IsAsciiLetter(code[0]) && char.IsAsciiLetter(code[1]);

    private static bool IsPublic(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal || address.IsIPv6Multicast);
        }
        var b = address.GetAddressBytes();
        return !(b[0] == 0 || b[0] == 10 || b[0] >= 224
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127));
    }
}
