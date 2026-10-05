using System.Net;
using System.Net.Sockets;

namespace Pulsatilla.Wpf;

public sealed record ThreatSignal(string Level, string Message);

/// <summary>Bounded, local heuristics over observed packets; signals request review, not a verdict.</summary>
public sealed class NetworkThreatMonitor
{
    private const int MaximumSources = 256, MaximumDomains = 1024;
    private readonly Dictionary<string, SynWindow> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DnsState> _domains = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _reported = new(StringComparer.OrdinalIgnoreCase);
    public bool Enabled { get; set; } = true;
    internal int TrackedSources => _sources.Count;
    internal int TrackedDomains => _domains.Count;

    public IReadOnlyList<ThreatSignal> Observe(PacketObservation packet, string localAddress)
    {
        if (!Enabled) return [];
        List<ThreatSignal> signals = [];
        var now = packet.CapturedAtUtc;
        if (packet.Protocol == "TCP" && packet.Destination == localAddress && packet.Source != localAddress &&
            (packet.TcpFlags & 0x12) == 0x02 && packet.DestinationPort > 0)
        {
            if (!_sources.TryGetValue(packet.Source, out var window) || now - window.Start >= TimeSpan.FromSeconds(15))
            {
                if (_sources.Count >= MaximumSources) _sources.Remove(_sources.MinBy(pair => pair.Value.Start).Key);
                _sources[packet.Source] = window = new SynWindow(now);
            }
            window.Count++;
            if (window.Ports.Count < 64) window.Ports.Add(packet.DestinationPort);
            if (packet.DestinationPort == 3389) window.RdpAttempts++;
            if (window.Ports.Count >= 12)
                Report(signals, "scan:" + packet.Source, now, "ATTACK REVIEW",
                    $"Possible inbound port scan from {packet.Source}: {window.Ports.Count} distinct TCP ports in 15 seconds. Verify the source; authorized scanners can do this too.");
            if (window.Count >= 150)
                Report(signals, "flood:" + packet.Source, now, "ATTACK REVIEW",
                    $"High inbound SYN rate from {packet.Source}: {window.Count} connection attempts in 15 seconds. Review for a flood or reconnecting client.");
            if (window.RdpAttempts >= 20)
                Report(signals, "rdp:" + packet.Source, now, "ATTACK REVIEW",
                    $"Repeated RDP connection attempts from {packet.Source}: {window.RdpAttempts} in 15 seconds. Authentication failures cannot be inferred from packets alone.");
        }

        // Routine CDN/load-balancer address rotation is ignored. Only a public-to-private transition is reviewed.
        if (packet.Destination == localAddress && packet.SourcePort == 53 && packet.DnsName is { Length: > 0 } domain &&
            packet.DnsAddresses.Length > 0 && !domain.EndsWith(".local", StringComparison.OrdinalIgnoreCase) &&
            !domain.EndsWith(".lan", StringComparison.OrdinalIgnoreCase) && domain.Contains('.'))
        {
            if (!_domains.TryGetValue(domain, out var state))
            {
                if (_domains.Count >= MaximumDomains) _domains.Remove(_domains.MinBy(pair => pair.Value.LastSeen).Key);
                _domains[domain] = state = new DnsState();
            }
            var hasPublic = packet.DnsAddresses.Any(IsPublicAddress);
            var hasPrivate = packet.DnsAddresses.Any(IsPrivateAddress);
            if (state.HadPublicAnswer && !state.HadPrivateAnswer && now >= state.LastSeen &&
                now - state.LastSeen < TimeSpan.FromMinutes(10) && hasPrivate)
                Report(signals, "dns:" + domain, now, "DNS ANOMALY",
                    $"Possible DNS rebinding for {domain}: an observed public answer changed to a private/local address. Split DNS and VPN routing can also cause this; verify before blocking.");
            if (now >= state.LastSeen)
            {
                state.HadPublicAnswer = hasPublic;
                state.HadPrivateAnswer = hasPrivate;
                state.LastSeen = now;
            }
        }
        return signals;
    }

    public void Reset() { _sources.Clear(); _domains.Clear(); _reported.Clear(); }

    private void Report(List<ThreatSignal> signals, string key, DateTime now, string level, string message)
    {
        if (_reported.TryGetValue(key, out var last) && now - last < TimeSpan.FromMinutes(2)) return;
        if (_reported.Count >= MaximumDomains) _reported.Remove(_reported.MinBy(pair => pair.Value).Key);
        _reported[key] = now;
        signals.Add(new(level, message));
    }

    private static bool IsPublicAddress(string value) => IPAddress.TryParse(value, out var ip) &&
        ip.AddressFamily == AddressFamily.InterNetwork && !IsPrivateAddress(value) && ip.GetAddressBytes()[0] is > 0 and < 224;

    private static bool IsPrivateAddress(string value)
    {
        if (!IPAddress.TryParse(value, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] is 0 or 10 or 127 || b[0] == 172 && b[1] is >= 16 and <= 31 ||
            b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254 || b[0] == 100 && b[1] is >= 64 and <= 127;
    }
    private sealed class SynWindow(DateTime start)
    {
        public DateTime Start { get; } = start;
        public int Count, RdpAttempts;
        public HashSet<int> Ports { get; } = [];
    }
    private sealed class DnsState { public bool HadPublicAnswer, HadPrivateAnswer; public DateTime LastSeen; }
}
