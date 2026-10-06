using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Pulsatilla.Wpf;

public sealed class ProcessConnectionResolver
{
    internal const int MaximumSockets = 8192;
    internal const int MaximumNativeRows = 100_000;
    internal const int MaximumNativeBytes = 32 * 1024 * 1024;
    private readonly object _sync = new();
    private Dictionary<string, ProcessIdentity?> _tcp = new(StringComparer.Ordinal);
    private Dictionary<string, ProcessIdentity?> _udp = new(StringComparer.Ordinal);
    private OwnedSocketSnapshot[] _sockets = [];
    public int TcpEndpointCount { get; private set; }
    public int UdpEndpointCount { get; private set; }
    public bool SnapshotTruncated { get; private set; }
    public DateTime SnapshotAtUtc { get; private set; }
    public IReadOnlyList<OwnedSocketSnapshot> CurrentOwnedSockets { get { lock (_sync) return Array.AsReadOnly(_sockets); } }

    public void Refresh()
    {
        if (!OperatingSystem.IsWindows()) { RefreshFromSockets([]); return; }
        var processes = new Dictionary<uint, ProcessIdentity>(); var sockets = new List<OwnedSocketSnapshot>(); var truncated = false;
        foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
        {
            foreach (var tcp in new[] { true, false })
            {
                if (sockets.Count >= MaximumSockets) { truncated = true; break; }
                var table = ReadTable(tcp ? GetExtendedTcpTable : GetExtendedUdpTable, tcp ? 5 : 1, (int)family);
                var rows = DecodeTable(table, tcp, family, pid =>
                {
                    if (!processes.TryGetValue(pid, out var identity)) processes[pid] = identity = GetProcessIdentity(pid);
                    return identity;
                }, out var clipped);
                truncated |= clipped;
                foreach (var row in rows)
                {
                    if (sockets.Count >= MaximumSockets) { truncated = true; break; }
                    sockets.Add(row);
                }
            }
        }
        RefreshFromSockets(sockets, truncated);
    }
    internal void RefreshFromSockets(IEnumerable<OwnedSocketSnapshot> sockets, bool truncated = false)
    {
        var tcp = new Dictionary<string, ProcessIdentity?>(StringComparer.Ordinal); var udp = new Dictionary<string, ProcessIdentity?>(StringComparer.Ordinal);
        var bounded = sockets.Take(MaximumSockets + 1).ToArray();
        if (bounded.Length > MaximumSockets) { bounded = bounded[..MaximumSockets]; truncated = true; }
        foreach (var row in bounded)
        {
            if (!ValidEndpoint(row.LocalAddress, row.LocalPort) || row.Identity.ProcessId <= 0) continue;
            if (row.Protocol == "TCP")
            {
                if (!ValidEndpoint(row.RemoteAddress, row.RemotePort)) continue;
                AddUnique(tcp, TupleKey(row.LocalAddress, row.LocalPort, row.RemoteAddress, row.RemotePort), row.Identity);
            }
            else if (row.Protocol == "UDP") AddUnique(udp, EndpointKey(row.LocalAddress, row.LocalPort), row.Identity);
        }
        lock (_sync)
        {
            _tcp = tcp; _udp = udp; _sockets = bounded; SnapshotTruncated = truncated; SnapshotAtUtc = DateTime.UtcNow;
            TcpEndpointCount = tcp.Count; UdpEndpointCount = udp.Count;
        }
    }
    public ProcessIdentity? Resolve(PacketObservation packet, IPAddress localAddress)
    {
        var local = localAddress.ToString(); string remote; int localPort; int remotePort;
        if (AddressEquals(packet.Source, local)) { remote = packet.Destination; localPort = packet.SourcePort; remotePort = packet.DestinationPort; }
        else if (AddressEquals(packet.Destination, local)) { remote = packet.Source; localPort = packet.DestinationPort; remotePort = packet.SourcePort; }
        else return null;
        if (!ValidEndpoint(local, localPort) || !ValidEndpoint(remote, remotePort) || localPort == 0) return null;
        lock (_sync)
        {
            if (SnapshotTruncated) return null; // Missing rows can hide shared bindings; do not guess an owner.
            if (packet.Protocol == "TCP") return _tcp.GetValueOrDefault(TupleKey(local, localPort, remote, remotePort));
            if (packet.Protocol != "UDP") return null;
            var exactKey = EndpointKey(local, localPort);
            var wildcard = EndpointKey(localAddress.AddressFamily == AddressFamily.InterNetwork ? "0.0.0.0" : "::", localPort);
            var exactFound = _udp.TryGetValue(exactKey, out var exact); var wildcardFound = _udp.TryGetValue(wildcard, out var any);
            if ((exactFound && exact is null) || (wildcardFound && any is null)) return null;
            if (exactFound && wildcardFound && exact?.ProcessId != any?.ProcessId) return null;
            return exactFound ? exact : wildcardFound ? any : null;
        }
    }
    private static void AddUnique(Dictionary<string, ProcessIdentity?> owners, string key, ProcessIdentity identity)
    {
        if (owners.TryGetValue(key, out var existing) && existing?.ProcessId != identity.ProcessId) owners[key] = null;
        else if (!owners.ContainsKey(key)) owners.Add(key, identity);
    }
    internal static OwnedSocketSnapshot[] DecodeTable(byte[] table, bool tcp, AddressFamily family, Func<uint, ProcessIdentity> identity, out bool truncated)
    {
        truncated = false;
        if (table.Length < 4 || table.Length > MaximumNativeBytes || family is not AddressFamily.InterNetwork and not AddressFamily.InterNetworkV6) return [];
        var count = BinaryPrimitives.ReadUInt32LittleEndian(table);
        var v6 = family == AddressFamily.InterNetworkV6; var size = tcp ? (v6 ? 56 : 24) : (v6 ? 28 : 12);
        if (count > MaximumNativeRows || (ulong)count * (uint)size + 4 > (ulong)table.Length) { truncated = true; return []; }
        truncated = count > MaximumSockets;
        var rows = new List<OwnedSocketSnapshot>();
        for (var i = 0; i < Math.Min(count, MaximumSockets); i++)
        {
            var bytes = table.AsSpan(4 + i * size, size);
            var local = v6 ? new IPAddress(bytes[..16], BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4))).ToString()
                : new IPAddress(bytes.Slice(tcp ? 4 : 0, 4)).ToString();
            var localPort = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(v6 ? 20 : tcp ? 8 : 4, 2));
            var remote = tcp ? (v6 ? new IPAddress(bytes.Slice(24, 16), BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(40, 4))).ToString() : new IPAddress(bytes.Slice(12, 4)).ToString()) : "";
            var remotePort = tcp ? BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(v6 ? 44 : 16, 2)) : 0;
            var pid = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(v6 ? tcp ? 52 : 24 : tcp ? 20 : 8, 4));
            var state = tcp ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(v6 ? 48 : 0, 4)) : 0;
            if (pid is 0 or > int.MaxValue) continue;
            rows.Add(new(identity(pid), tcp ? "TCP" : "UDP", local, localPort, remote, remotePort,
                tcp ? TcpStateLabel(state) : "Bound UDP endpoint (connection state unknown)", tcp && state == 5));
        }
        return rows.ToArray();
    }
    internal static bool NativeSizeAllowed(uint size) => size >= 4 && size <= MaximumNativeBytes;
    private static byte[] ReadTable(TableReader reader, int tableClass, int family)
    {
        uint size = 0; var result = reader(IntPtr.Zero, ref size, true, family, tableClass, 0);
        if (!NativeSizeAllowed(size) || result is not 0 and not 122) return [];
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var buffer = new byte[size]; var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                result = reader(pinned.AddrOfPinnedObject(), ref size, true, family, tableClass, 0);
                if (result == 0 && size <= buffer.Length) return buffer;
                if (result != 122 || !NativeSizeAllowed(size)) return [];
            }
            finally { pinned.Free(); }
        }
        return [];
    }
    private static ProcessIdentity GetProcessIdentity(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid); string path = "";
            try { path = process.MainModule?.FileName ?? ""; } catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }
            return new((int)pid, process.ProcessName, path);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return new((int)pid, "Unknown process", ""); }
    }
    private static string TcpStateLabel(uint state) => state switch
    { 1 => "Closed", 2 => "Listen", 3 => "SynSent", 4 => "SynReceived", 5 => "Established", 6 => "FinWait1", 7 => "FinWait2", 8 => "CloseWait", 9 => "Closing", 10 => "LastAck", 11 => "TimeWait", 12 => "DeleteTCB", _ => "Unknown" };
    private static bool ValidEndpoint(string address, int port) => port is >= 0 and <= 65535 && IPAddress.TryParse(address, out _);
    private static bool AddressEquals(string left, string right) => IPAddress.TryParse(left, out var a) && IPAddress.TryParse(right, out var b) && a.Equals(b);
    private static string EndpointKey(string address, int port) => $"{IPAddress.Parse(address)}|{port}";
    private static string TupleKey(string local, int localPort, string remote, int remotePort) => $"{EndpointKey(local, localPort)}|{EndpointKey(remote, remotePort)}";
    private delegate uint TableReader(IntPtr table, ref uint size, bool order, int addressFamily, int tableClass, uint reserved);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, bool order, int addressFamily, int tableClass, uint reserved);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, bool order, int addressFamily, int tableClass, uint reserved);
}
public sealed record ProcessIdentity(int ProcessId, string Name, string ExecutablePath);
public sealed record OwnedSocketSnapshot(ProcessIdentity Identity, string Protocol, string LocalAddress, int LocalPort,
    string RemoteAddress, int RemotePort, string Status, bool IsActiveConnection);
