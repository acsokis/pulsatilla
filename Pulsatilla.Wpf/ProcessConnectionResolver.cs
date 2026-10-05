using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;

namespace Pulsatilla.Wpf;

public sealed class ProcessConnectionResolver
{
    private readonly object _sync = new();
    private Dictionary<string, ProcessIdentity> _tcp = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ProcessIdentity> _udp = new(StringComparer.OrdinalIgnoreCase);
    public int TcpEndpointCount { get; private set; }
    public int UdpEndpointCount { get; private set; }

    public void Refresh()
    {
        var processes = new Dictionary<uint, ProcessIdentity>();
        var tcp = ReadTcpOwners(processes);
        var udp = ReadUdpOwners(processes);
        lock (_sync)
        {
            _tcp = tcp;
            _udp = udp;
            TcpEndpointCount = tcp.Count;
            UdpEndpointCount = udp.Count;
        }
    }

    public ProcessIdentity? Resolve(PacketObservation packet, IPAddress localAddress)
    {
        var localIp = localAddress.ToString();
        string? localEndpoint = null;
        if (packet.Source.Equals(localIp, StringComparison.OrdinalIgnoreCase))
            localEndpoint = EndpointKey(localIp, packet.SourcePort);
        else if (packet.Destination.Equals(localIp, StringComparison.OrdinalIgnoreCase))
            localEndpoint = EndpointKey(localIp, packet.DestinationPort);
        if (localEndpoint is null) return null;

        lock (_sync)
        {
            var owners = packet.Protocol == "TCP" ? _tcp : packet.Protocol == "UDP" ? _udp : null;
            if (owners is null) return null;
            if (owners.TryGetValue(localEndpoint, out var identity)) return identity;
            var portSeparator = localEndpoint.LastIndexOf(':');
            return portSeparator >= 0 && owners.TryGetValue("0.0.0.0" + localEndpoint[portSeparator..], out identity)
                ? identity
                : null;
        }
    }

    private static Dictionary<string, ProcessIdentity> ReadTcpOwners(Dictionary<uint, ProcessIdentity> processes)
    {
        const int ownerPidAll = 5;
        var table = ReadTable(GetExtendedTcpTable, ownerPidAll);
        var result = new Dictionary<string, ProcessIdentity>(StringComparer.OrdinalIgnoreCase);
        if (table.Length == 0) return result;

        var count = BitConverter.ToUInt32(table, 0);
        var rowSize = Marshal.SizeOf<TcpRowOwnerPid>();
        var pointer = Marshal.AllocHGlobal(table.Length);
        try
        {
            Marshal.Copy(table, 0, pointer, table.Length);
            var rowPointer = IntPtr.Add(pointer, sizeof(uint));
            for (var index = 0; index < count; index++, rowPointer = IntPtr.Add(rowPointer, rowSize))
            {
                var row = Marshal.PtrToStructure<TcpRowOwnerPid>(rowPointer);
                AddOwner(result, row.LocalAddress, row.LocalPort, row.ProcessId, processes);
            }
        }
        finally { Marshal.FreeHGlobal(pointer); }
        return result;
    }

    private static Dictionary<string, ProcessIdentity> ReadUdpOwners(Dictionary<uint, ProcessIdentity> processes)
    {
        const int ownerPid = 1;
        var table = ReadTable(GetExtendedUdpTable, ownerPid);
        var result = new Dictionary<string, ProcessIdentity>(StringComparer.OrdinalIgnoreCase);
        if (table.Length == 0) return result;

        var count = BitConverter.ToUInt32(table, 0);
        var rowSize = Marshal.SizeOf<UdpRowOwnerPid>();
        var pointer = Marshal.AllocHGlobal(table.Length);
        try
        {
            Marshal.Copy(table, 0, pointer, table.Length);
            var rowPointer = IntPtr.Add(pointer, sizeof(uint));
            for (var index = 0; index < count; index++, rowPointer = IntPtr.Add(rowPointer, rowSize))
            {
                var row = Marshal.PtrToStructure<UdpRowOwnerPid>(rowPointer);
                AddOwner(result, row.LocalAddress, row.LocalPort, row.ProcessId, processes);
            }
        }
        finally { Marshal.FreeHGlobal(pointer); }
        return result;
    }

    private static byte[] ReadTable(TableReader reader, int tableClass)
    {
        uint size = 0;
        var result = reader(IntPtr.Zero, ref size, true, 2, tableClass, 0);
        if (size == 0 || (result != 0 && result != 122)) return [];
        var buffer = new byte[size];
        var pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
        try
        {
            result = reader(pinned.AddrOfPinnedObject(), ref size, true, 2, tableClass, 0);
            return result == 0 ? buffer : [];
        }
        finally { pinned.Free(); }
    }

    private static void AddOwner(Dictionary<string, ProcessIdentity> map, uint localAddress, uint localPort, uint processId,
        Dictionary<uint, ProcessIdentity> processes)
    {
        var address = new IPAddress(BitConverter.GetBytes(localAddress)).ToString();
        var port = NetworkPortToHost(localPort);
        if (!processes.TryGetValue(processId, out var identity))
            processes[processId] = identity = GetProcessIdentity(processId);
        map[EndpointKey(address, port)] = identity;
        map[EndpointKey("0.0.0.0", port)] = identity;
    }

    private static ProcessIdentity GetProcessIdentity(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            var path = string.Empty;
            try { path = process.MainModule?.FileName ?? string.Empty; }
            catch (Exception) { }
            return new ProcessIdentity((int)processId, process.ProcessName, path);
        }
        catch (Exception) { return new ProcessIdentity((int)processId, "Unknown process", string.Empty); }
    }

    private static int NetworkPortToHost(uint value)
    {
        var port = (ushort)(value & 0xFFFF);
        return ((port & 0xFF) << 8) | (port >> 8);
    }

    private static string EndpointKey(string address, int port) => $"{address}:{port}";

    private delegate uint TableReader(IntPtr table, ref uint size, bool order, int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr table, ref uint size, bool order, int addressFamily, int tableClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr table, ref uint size, bool order, int addressFamily, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct TcpRowOwnerPid
    {
        public uint State;
        public uint LocalAddress;
        public uint LocalPort;
        public uint RemoteAddress;
        public uint RemotePort;
        public uint ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UdpRowOwnerPid
    {
        public uint LocalAddress;
        public uint LocalPort;
        public uint ProcessId;
    }
}

public sealed record ProcessIdentity(int ProcessId, string Name, string ExecutablePath);