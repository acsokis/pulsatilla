using System.Text;
using System.Text.RegularExpressions;

namespace Pulsatilla.Wpf;

/// <summary>Small reviewed subset, not a permissive OpenVPN configuration interpreter.</summary>
public static class OpenVpnProfileValidator
{
    public const int MaximumBytes = 262144;
    private static readonly HashSet<string> Inline = new(StringComparer.Ordinal) { "ca", "cert", "key", "tls-auth", "tls-crypt" };
    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal)
        { "client", "nobind", "persist-key", "persist-tun", "remote-random", "auth-nocache", "pull", "explicit-exit-notify" };
    public static VpnProfileValidation Validate(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaximumBytes || text.Contains('\0'))
            return Reject("The profile is empty, oversized or contains a NUL character.");
        var output = new StringBuilder(); var blocks = new HashSet<string>();
        string? block = null; var remote = ""; var auth = false; var client = false; var tls = false; var minimumTlsProvided = false;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        if (lines.Length > 4096) return Reject("Too many profile lines.");
        foreach (var original in lines)
        {
            var line = original.Trim();
            if (line.Length > 4096 || line.Any(c => char.IsControl(c) && c != '\t')) return Reject("Invalid or oversized profile line.");
            if (block is not null)
            {
                if (line == $"</{block}>") { output.AppendLine(line); block = null; continue; }
                // PEM material only, never nested configuration or options inside a block.
                if (line.StartsWith('<') || !Regex.IsMatch(line, @"^(?:[A-Za-z0-9+/=]+|-----BEGIN [A-Za-z0-9 \-]+-----|-----END [A-Za-z0-9 \-]+-----|#[A-Za-z0-9 \-]*|)$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    return Reject("Invalid inline certificate/key block.");
                output.AppendLine(line); continue;
            }
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';')) continue;
            if (line.StartsWith('<'))
            {
                var name = line.Length > 2 && line.EndsWith('>') ? line[1..^1] : "";
                if (!Inline.Contains(name) || !blocks.Add(name)) return Reject("Unsupported or repeated inline block.");
                block = name; output.AppendLine(line); continue;
            }
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var option = parts[0];
            if (Flags.Contains(option) && parts.Length == 1) { client |= option == "client"; output.AppendLine(line); continue; }
            var accepted = option switch
            {
                "dev" => parts.Length == 2 && parts[1] is "tun" or "tap",
                "proto" => parts.Length == 2 && parts[1] is "udp" or "udp4" or "udp6" or "tcp-client" or "tcp4-client" or "tcp6-client",
                "remote" => parts.Length is >= 2 and <= 4 && IsHost(parts[1]) &&
                    (parts.Length < 3 || int.TryParse(parts[2], out var port) && port is > 0 and <= 65535) &&
                    (parts.Length < 4 || parts[3] is "udp" or "udp4" or "udp6" or "tcp-client" or "tcp4-client" or "tcp6-client"),
                "remote-cert-tls" => parts.Length == 2 && parts[1] == "server",
                "tls-version-min" => parts.Length == 2 && parts[1] is "1.2" or "1.3",
                "data-ciphers" => parts.Length == 2 && parts[1].Split(':').All(c => c is "AES-256-GCM" or "AES-128-GCM" or "CHACHA20-POLY1305"),
                "cipher" => parts.Length == 2 && parts[1] is "AES-256-GCM" or "AES-128-GCM",
                "auth" => parts.Length == 2 && parts[1] is "SHA256" or "SHA384" or "SHA512",
                "resolv-retry" => parts.Length == 2 && parts[1] == "infinite",
                "verb" => parts.Length == 2 && parts[1] is "0" or "1" or "2" or "3",
                "key-direction" => parts.Length == 2 && parts[1] is "0" or "1",
                "auth-user-pass" => parts.Length == 1,
                _ => false
            };
            if (!accepted) return Reject("Unsupported profile directive: " + option[..Math.Min(option.Length, 48)] + ". External commands/files and unreviewed options are blocked.");
            if (option == "remote" && remote.Length == 0) remote = parts[1];
            if (option == "remote-cert-tls") tls = true;
            if (option == "auth-user-pass") auth = true;
            if (option == "tls-version-min") minimumTlsProvided = true;
            output.AppendLine(line);
        }
        if (block is not null || !client || remote.Length == 0 || !tls || !blocks.Contains("ca"))
            return Reject("A client profile, remote endpoint, server certificate verification and inline CA are required.");
        if (!auth && (!blocks.Contains("cert") || !blocks.Contains("key")))
            return Reject("Certificate profiles require inline certificate and key.");
        if (text.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal) || text.Contains("Proc-Type: 4,ENCRYPTED", StringComparison.Ordinal))
            return Reject("Encrypted private-key prompting is not supported by this integration.");
        // Enforce local policy after parsing; profile cannot override these.
        output.AppendLine("script-security 1").AppendLine("auth-nocache");
        if (!minimumTlsProvided) output.AppendLine("tls-version-min 1.2");
        output.AppendLine("verb 3");
        return new(true, output.ToString(), auth, remote,
            auth ? ["Imported; username/password management authentication is not implemented. Connect is disabled."] :
                ["Experimental certificate-profile integration. Import is not proof of server trust or anonymity."]);
    }
    private static bool IsHost(string value) => value.Length <= 253 &&
        Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9.\-:]*$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
    private static VpnProfileValidation Reject(string reason) => new(false, "", false, "", [reason]);
}
