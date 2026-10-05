using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pulsatilla.Wpf;

public static class TrafficFormat
{
    public static string Bytes(double value)
    {
        value = double.IsFinite(value) ? Math.Max(0, value) : 0;
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB" }; var index = 0;
        while (value >= 1024 && index < units.Length - 1) { value /= 1024; index++; }
        return $"{value:0.##} {units[index]}";
    }
    public static string Rate(double value) => Bytes(value) + "/s";
}

public sealed record TrafficSourceSnapshot(string Key, string Name, long Packets, long Bytes, long DownloadBytes, long UploadBytes, IReadOnlyList<string> Details);

public sealed class TrafficSourceNode(string key) : INotifyPropertyChanged
{
    public string Key { get; } = key;
    public string Header { get; private set; } = key;
    public double Weight { get; private set; }
    public string Identity { get; private set; } = "";
    public bool IsExpanded { get; set; }
    public ObservableCollection<string> Details { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    private TrafficSourceSnapshot? _snapshot;
    public void Update(TrafficSourceSnapshot snapshot, double maximum, string identity = "", bool byBytes = false)
    {
        _snapshot = snapshot; Identity = identity;
        Header = snapshot.Name + (identity.Length > 0 ? " · " + identity : "") + $"  ·  {snapshot.Packets:N0} packets / {TrafficFormat.Bytes(snapshot.Bytes)}";
        Weight = maximum > 0 ? (byBytes ? snapshot.Bytes : snapshot.Packets) / maximum : 0;
        Details.Clear(); foreach (var detail in snapshot.Details) Details.Add(detail);
        Changed(nameof(Header)); Changed(nameof(Weight)); Changed(nameof(Identity));
    }
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
}

/// <summary>Bounded in-memory packet/source aggregation. Application names describe local endpoint owners.</summary>
public sealed class TrafficSourceTracker
{
    private readonly Dictionary<string, SourceState> _ips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SourceState> _apps = new(StringComparer.OrdinalIgnoreCase);
    internal int IpCount => _ips.Count;
    internal int ApplicationCount => _apps.Count;
    public IReadOnlyDictionary<string, long> SourcePacketCounts => _ips.ToDictionary(pair => pair.Key, pair => pair.Value.Packets, StringComparer.OrdinalIgnoreCase);
    public void Observe(PacketObservation packet, string? localIp, string application = "Unattributed", string executablePath = "")
    {
        if (packet.Length <= 0) return;
        var outbound = packet.Source.Equals(localIp, StringComparison.OrdinalIgnoreCase);
        var inbound = packet.Destination.Equals(localIp, StringComparison.OrdinalIgnoreCase);
        if (!_ips.TryGetValue(packet.Source, out var source) && _ips.Count < 1024)
            _ips[packet.Source] = source = new SourceState(packet.Source);
        source?.Add(packet, outbound, inbound, inbound || outbound ? application : null, packet.Destination);
        if (!inbound && !outbound) return;
        var key = executablePath.Length > 0 ? executablePath : application;
        if (!_apps.TryGetValue(key, out var app) && _apps.Count < 256)
            _apps[key] = app = new SourceState(application, executablePath);
        app?.Add(packet, outbound, inbound, null, outbound ? packet.Destination : packet.Source);
    }
    public IReadOnlyList<TrafficSourceSnapshot> TopIps() => _ips.OrderByDescending(pair => pair.Value.Packets)
        .ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(8).Select(pair => pair.Value.Snapshot(pair.Key, false)).ToArray();
    public IReadOnlyList<TrafficSourceSnapshot> TopApplications() => _apps.OrderByDescending(pair => pair.Value.Bytes)
        .ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(8).Select(pair => pair.Value.Snapshot(pair.Key, true)).ToArray();

    private sealed class SourceState(string name, string path = "")
    {
        public long Packets, Bytes, Download, Upload;
        private readonly HashSet<string> _protocols = new(StringComparer.Ordinal);
        private readonly HashSet<int> _ports = [];
        private readonly Dictionary<string, long> _applications = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _peers = new(StringComparer.OrdinalIgnoreCase);
        public void Add(PacketObservation packet, bool outbound, bool inbound, string? application, string peer)
        {
            Packets++; Bytes += packet.Length;
            if (outbound) Upload += packet.Length; else if (inbound) Download += packet.Length;
            if (_protocols.Count < 8) _protocols.Add(packet.Protocol);
            if (_ports.Count < 32 && packet.SourcePort > 0) _ports.Add(packet.SourcePort);
            if (application is not null && (_applications.Count < 32 || _applications.ContainsKey(application)))
                _applications[application] = _applications.GetValueOrDefault(application) + packet.Length;
            if (_peers.Count < 32 || _peers.ContainsKey(peer)) _peers[peer] = _peers.GetValueOrDefault(peer) + packet.Length;
        }
        public TrafficSourceSnapshot Snapshot(string key, bool app)
        {
            var details = new List<string> { $"Download: {TrafficFormat.Bytes(Download)}  ·  Upload: {TrafficFormat.Bytes(Upload)}",
                "Protocols: " + string.Join(", ", _protocols.Order()), "Observed source ports (first 32): " + string.Join(", ", _ports.Order()) };
            if (app && path.Length > 0) details.Add("Executable: " + path);
            foreach (var pair in (app ? _peers : _applications).OrderByDescending(pair => pair.Value).Take(8))
                details.Add((app ? "Remote IP: " : "Local program: ") + pair.Key + "  ·  " + TrafficFormat.Bytes(pair.Value));
            return new(key, name, Packets, Bytes, Download, Upload, details);
        }
    }
}
