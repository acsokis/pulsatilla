using System.Globalization;
using System.Net.Mail;

namespace Pulsatilla.Wpf;

internal static class EmailAddressRules
{
    public static IEnumerable<string> Tokens(string value) => value.Split([',', ';', '\r', '\n'],
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static string? Address(string value)
    {
        try
        {
            var address = new MailAddress(value).Address;
            var at = address.LastIndexOf('@');
            if (at <= 0 || at == address.Length - 1) return null;
            var domain = NormalizeDomain(address[(at + 1)..]);
            return domain is null ? null : address[..at].ToLowerInvariant() + "@" + domain;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return null; }
    }

    public static IEnumerable<string> Addresses(string value)
    {
        var addresses = new MailAddressCollection();
        try { addresses.Add(value.Replace(';', ',')); }
        catch (FormatException) { return Tokens(value).Select(Address).OfType<string>(); }
        catch (ArgumentException) { return []; }
        return addresses.Select(address => Address(address.Address)).OfType<string>();
    }

    public static string Domain(string address) => address[(address.LastIndexOf('@') + 1)..];

    public static bool MatchesSender(string? sender, string entries) => sender is not null && Tokens(entries).Any(entry =>
        entry.Contains('@') ? Address(entry) == sender :
            string.Equals(Domain(sender), NormalizeDomain(entry.TrimStart('.')), StringComparison.Ordinal));

    private static string? NormalizeDomain(string domain)
    {
        try { return new IdnMapping().GetAscii(domain).ToLowerInvariant(); }
        catch (ArgumentException) { return null; }
    }
}
