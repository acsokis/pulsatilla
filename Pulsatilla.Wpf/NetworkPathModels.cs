using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Pulsatilla.Wpf;

public enum AdapterClassification { Physical, Virtual, LayeredTunnel, Loopback, Disconnected, Unknown }
public enum InterfaceSelectionMode { Auto, Manual }
public sealed record NetworkAdapterInfo(string Id, string Name, string Description, int InterfaceIndex,
    int Ipv6InterfaceIndex, OperationalStatus Status, NetworkInterfaceType InterfaceType,
    AdapterClassification Classification, string[] Addresses, string[] Gateways, string[] DnsServers,
    bool? HardwareInterface, uint TunnelType)
{
    public string Group => Status != OperationalStatus.Up ? "DISCONNECTED" : Classification switch
    { AdapterClassification.Physical => "PHYSICAL ADAPTERS", AdapterClassification.LayeredTunnel => "LAYERED / TUNNEL ADAPTERS",
      AdapterClassification.Virtual => "VIRTUAL ADAPTERS", AdapterClassification.Loopback => "LOOPBACK", _ => "UNKNOWN" };
    public string LocalIpv4 => Addresses.FirstOrDefault(a => IPAddress.TryParse(a, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork) ?? "Unknown";
}
public sealed record NetworkRouteInfo(string Destination, int PrefixLength, string Gateway, int InterfaceIndex,
    AddressFamily Family, uint RouteMetric, uint? InterfaceMetric, bool IsDefault, bool IsTunnel, string InterfaceName)
{
    public ulong? EffectiveMetric => InterfaceMetric is { } metric ? (ulong)metric + RouteMetric : null;
    public string Prefix => $"{Destination}/{PrefixLength}";
    public string FamilyLabel => Family == AddressFamily.InterNetwork ? "IPv4" : "IPv6";
    public string Flags => (IsDefault ? "Default route" : "") + (IsTunnel ? " / Tunnel" : "");
}
public sealed record InterfaceMetricState(int InterfaceIndex, AddressFamily Family, bool Automatic, uint Metric);
public sealed record NetworkTopologySnapshot(NetworkAdapterInfo[] Adapters, NetworkRouteInfo[] Routes,
    InterfaceMetricState[] Metrics, int? BestIpv4Index, int? BestIpv6Index,
    (int Higher, int Lower)[] Layers, string? Error, DateTime ObservedAtUtc);
public sealed record NetworkPathSnapshot(NetworkTopologySnapshot Topology, NetworkAdapterInfo? SelectedAdapter,
    NetworkAdapterInfo? EffectiveIpv4Adapter, NetworkAdapterInfo? EffectiveIpv6Adapter,
    NetworkAdapterInfo? PhysicalUnderlay, InterfaceSelectionMode SelectionMode, string SelectionReason)
{
    public string UnderlayLabel => PhysicalUnderlay?.Name ?? "Unknown (no verified interface-stack relationship)";
    public string RouteLabel => EffectiveIpv4Adapter?.Name ?? "No effective IPv4 route found";
}
public sealed class NetworkPathChangedEventArgs(NetworkPathSnapshot snapshot) : EventArgs
{ public NetworkPathSnapshot Snapshot { get; } = snapshot; }

public static class AdapterClassifier
{
    public static AdapterClassification Classify(NetworkInterfaceType type, bool? hardware, uint tunnelType, string description)
    {
        if (type == NetworkInterfaceType.Loopback) return AdapterClassification.Loopback;
        if (type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp || tunnelType != 0) return AdapterClassification.LayeredTunnel;
        // Description is supplementary to Windows' non-hardware flag, never the only evidence.
        if (hardware == false && new[] { "wireguard", "openvpn", "tap-windows", "wintun", "vpn" }
            .Any(word => description.Contains(word, StringComparison.OrdinalIgnoreCase))) return AdapterClassification.LayeredTunnel;
        if (hardware == true) return AdapterClassification.Physical;
        if (hardware == false) return AdapterClassification.Virtual;
        return AdapterClassification.Unknown;
    }
}

public static class NetworkPathSelector
{
    public static NetworkPathSnapshot Select(NetworkTopologySnapshot topology, InterfaceSelectionMode mode, string? manualId)
    {
        NetworkAdapterInfo? Find(int? index, bool v6) => topology.Adapters.FirstOrDefault(a => a.Status == OperationalStatus.Up
            && a.Classification != AdapterClassification.Loopback && (v6 ? a.Ipv6InterfaceIndex : a.InterfaceIndex) == index);
        var ipv4 = Find(topology.BestIpv4Index, false);
        var ipv6 = Find(topology.BestIpv6Index, true);
        var selected = mode == InterfaceSelectionMode.Manual ? topology.Adapters.FirstOrDefault(a => a.Id == manualId
            && a.Status == OperationalStatus.Up && a.Classification != AdapterClassification.Loopback) : null;
        var reason = selected is not null ? "Manual selection (does not change Windows routing)" : "Windows effective IPv4 route lookup (1.1.1.1; no packet sent)";
        selected ??= ipv4;
        if (selected is null && ipv6 is not null) { selected = ipv6; reason = "Windows effective IPv6 route lookup (2606:4700:4700::1111; no packet sent)"; }
        if (selected is null)
        {
            selected = topology.Adapters.Where(a => a.Status == OperationalStatus.Up && a.Classification != AdapterClassification.Loopback && a.Addresses.Length > 0)
                .OrderByDescending(a => a.Gateways.Length > 0).ThenBy(a => a.InterfaceIndex).FirstOrDefault();
            reason = selected is null ? "No operational interface available" : "Operational fallback; Internet route not verified";
        }
        if (mode == InterfaceSelectionMode.Manual && selected?.Id != manualId) reason = "Manual interface unavailable; " + reason;
        NetworkAdapterInfo? physical = selected?.Classification == AdapterClassification.Physical ? selected : null;
        if (physical is null && selected is not null)
        {
            var visited = new HashSet<int>(); var pending = new Queue<int>(); var physicals = new HashSet<string>();
            pending.Enqueue(selected.InterfaceIndex);
            while (pending.Count > 0 && visited.Count < 64)
            {
                var current = pending.Dequeue(); if (!visited.Add(current)) continue;
                foreach (var layer in topology.Layers.Where(l => l.Higher == current))
                {
                    var adapter = topology.Adapters.FirstOrDefault(a => a.InterfaceIndex == layer.Lower);
                    if (adapter?.Classification == AdapterClassification.Physical && adapter.Status == OperationalStatus.Up) physicals.Add(adapter.Id);
                    else pending.Enqueue(layer.Lower);
                }
            }
            if (physicals.Count == 1) physical = topology.Adapters.First(a => a.Id == physicals.Single());
        }
        return new(topology, selected, ipv4, ipv6, physical, mode, reason);
    }
}
