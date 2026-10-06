using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Pulsatilla.Wpf;

public interface INetworkTopologyReader { NetworkTopologySnapshot Read(); }
public interface IInterfaceMetricBackend
{ InterfaceMetricState Read(int index, AddressFamily family); void Write(InterfaceMetricState state); }

/// <summary>Local IP Helper reads; route lookup does not send probes, resolve DNS, or change routes.</summary>
public sealed class WindowsNetworkTopology : INetworkTopologyReader, IInterfaceMetricBackend
{
    public NetworkTopologySnapshot Read()
    {
        var adapters = new List<NetworkAdapterInfo>(); var metrics = new List<InterfaceMetricState>();
        var routes = new List<NetworkRouteInfo>(); string? error = null;
        foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Take(512))
        {
            try
            {
                var properties = adapter.GetIPProperties();
                var index = adapter.Supports(NetworkInterfaceComponent.IPv4) ? properties.GetIPv4Properties()?.Index ?? 0 : 0;
                var index6 = adapter.Supports(NetworkInterfaceComponent.IPv6) ? properties.GetIPv6Properties()?.Index ?? 0 : 0;
                var flags = ReadInterfaceFacts(index > 0 ? index : index6);
                adapters.Add(new(adapter.Id, adapter.Name, adapter.Description, index, index6, adapter.OperationalStatus,
                    adapter.NetworkInterfaceType, AdapterClassifier.Classify(adapter.NetworkInterfaceType, flags.Hardware, flags.Tunnel, adapter.Description),
                    properties.UnicastAddresses.Select(a => a.Address.ToString()).Take(32).ToArray(),
                    properties.GatewayAddresses.Select(a => a.Address.ToString()).Take(32).ToArray(),
                    properties.DnsAddresses.Select(a => a.ToString()).Take(32).ToArray(), flags.Hardware, flags.Tunnel));
                foreach (var family in new[] { AddressFamily.InterNetwork, AddressFamily.InterNetworkV6 })
                {
                    var familyIndex = family == AddressFamily.InterNetwork ? index : index6;
                    if (familyIndex > 0) try { metrics.Add(Read(familyIndex, family)); } catch (Win32Exception) { }
                }
            }
            catch (NetworkInformationException) { error = "Some adapters changed during refresh; refresh again."; }
        }
        if (OperatingSystem.IsWindows())
        {
            var result = GetIpForwardTable2(0, out var table);
            if (result != 0) error = $"Windows route table unavailable (error {result}).";
            else try
            {
                var count = Marshal.ReadInt32(table);
                if (count < 0 || count > 100_000) throw new InvalidOperationException("Route table exceeds safety limit.");
                for (var i = 0; i < Math.Min(count, 4096); i++)
                {
                    var row = Marshal.PtrToStructure<RouteRow>(IntPtr.Add(table, 8 + i * 104));
                    if (row.Destination.Address.Family is not 2 and not 23 || row.ValidLifetime == 0) continue;
                    var family = (AddressFamily)row.Destination.Address.Family;
                    var adapter = adapters.FirstOrDefault(a => (family == AddressFamily.InterNetwork ? a.InterfaceIndex : a.Ipv6InterfaceIndex) == row.InterfaceIndex);
                    var metric = metrics.FirstOrDefault(m => m.InterfaceIndex == row.InterfaceIndex && m.Family == family);
                    routes.Add(new(row.Destination.Address.ToAddress().ToString(), row.Destination.Length, row.NextHop.ToAddress().ToString(),
                        (int)row.InterfaceIndex, family, row.Metric, metric?.Metric, row.Destination.Length == 0,
                        adapter?.Classification == AdapterClassification.LayeredTunnel, adapter?.Name ?? $"Interface {row.InterfaceIndex}"));
                }
                if (count > 4096) error = "Route view truncated to 4096 entries.";
            }
            finally { FreeMibTable(table); }
        }
        return new(adapters.ToArray(), routes.ToArray(), metrics.ToArray(), BestIndex(IPAddress.Parse("1.1.1.1")),
            BestIndex(IPAddress.Parse("2606:4700:4700::1111")), ReadLayers(), error, DateTime.UtcNow);
    }

    public InterfaceMetricState Read(int index, AddressFamily family)
    {
        ValidateFamily(index, family);
        var row = new IpInterfaceRow { Family = (ushort)family, Index = (uint)index };
        var result = GetIpInterfaceEntry(ref row); if (result != 0) throw new Win32Exception((int)result);
        return new(index, family, row.AutomaticMetric != 0, row.Metric);
    }
    public void Write(InterfaceMetricState state)
    {
        ValidateFamily(state.InterfaceIndex, state.Family);
        var row = new IpInterfaceRow { Family = (ushort)state.Family, Index = (uint)state.InterfaceIndex };
        var result = GetIpInterfaceEntry(ref row); if (result != 0) throw new Win32Exception((int)result);
        row.AutomaticMetric = state.Automatic ? (byte)1 : (byte)0; row.Metric = state.Metric;
        if (state.Family == AddressFamily.InterNetwork) row.SitePrefixLength = 0;
        result = SetIpInterfaceEntry(ref row); if (result != 0) throw new Win32Exception((int)result);
    }
    private static void ValidateFamily(int index, AddressFamily family)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Windows IP Helper is required.");
        if (index <= 0 || family is not AddressFamily.InterNetwork and not AddressFamily.InterNetworkV6) throw new ArgumentOutOfRangeException(nameof(index));
    }
    private static (bool? Hardware, uint Tunnel) ReadInterfaceFacts(int index)
    {
        if (!OperatingSystem.IsWindows() || index <= 0) return (null, 0);
        var memory = Marshal.AllocHGlobal(1352);
        try
        {
            Marshal.Copy(new byte[1352], 0, memory, 1352); Marshal.WriteInt32(memory, 8, index);
            return GetIfEntry2(memory) == 0 ? ((Marshal.ReadByte(memory, 1152) & 1) != 0, unchecked((uint)Marshal.ReadInt32(memory, 1132))) : (null, 0);
        }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static int? BestIndex(IPAddress destination)
    {
        if (!OperatingSystem.IsWindows()) return null;
        var address = SocketAddressNative.FromAddress(destination);
        return GetBestRoute2(IntPtr.Zero, 0, IntPtr.Zero, ref address, 0, out var row, out _) == 0 ? (int)row.InterfaceIndex : null;
    }
    private static (int Higher, int Lower)[] ReadLayers()
    {
        if (!OperatingSystem.IsWindows() || GetIfStackTable(out var table) != 0) return [];
        try
        {
            var count = Marshal.ReadInt32(table); if (count < 0 || count > 4096) return [];
            return Enumerable.Range(0, count).Select(i => (Marshal.ReadInt32(table, 4 + i * 8), Marshal.ReadInt32(table, 8 + i * 8))).ToArray();
        }
        finally { FreeMibTable(table); }
    }
    [StructLayout(LayoutKind.Explicit, Size = 28)]
    internal struct SocketAddressNative
    {
        [FieldOffset(0)] public ushort Family;
        [FieldOffset(4)] public uint Ipv4;
        [FieldOffset(8)] public ulong Ipv6First;
        [FieldOffset(16)] public ulong Ipv6Last;
        [FieldOffset(24)] public uint Scope;
        public IPAddress ToAddress() => Family == 2 ? new IPAddress(BitConverter.GetBytes(Ipv4)) : new IPAddress(BitConverter.GetBytes(Ipv6First).Concat(BitConverter.GetBytes(Ipv6Last)).ToArray(), Scope);
        public static SocketAddressNative FromAddress(IPAddress address)
        {
            var bytes = address.GetAddressBytes();
            return bytes.Length == 4 ? new() { Family = 2, Ipv4 = BitConverter.ToUInt32(bytes) }
                : new() { Family = 23, Ipv6First = BitConverter.ToUInt64(bytes), Ipv6Last = BitConverter.ToUInt64(bytes, 8), Scope = (uint)address.ScopeId };
        }
    }
    [StructLayout(LayoutKind.Explicit, Size = 32)]
    internal struct AddressPrefix { [FieldOffset(0)] public SocketAddressNative Address; [FieldOffset(28)] public byte Length; }
    [StructLayout(LayoutKind.Explicit, Size = 104)]
    internal struct RouteRow
    {
        [FieldOffset(0)] public ulong Luid;
        [FieldOffset(8)] public uint InterfaceIndex;
        [FieldOffset(12)] public AddressPrefix Destination;
        [FieldOffset(44)] public SocketAddressNative NextHop;
        [FieldOffset(76)] public uint ValidLifetime;
        [FieldOffset(84)] public uint Metric;
    }
    [StructLayout(LayoutKind.Explicit, Size = 168)]
    internal struct IpInterfaceRow
    {
        [FieldOffset(0)] public ushort Family;
        [FieldOffset(8)] public ulong Luid;
        [FieldOffset(16)] public uint Index;
        [FieldOffset(44)] public byte AutomaticMetric;
        [FieldOffset(144)] public uint SitePrefixLength;
        [FieldOffset(148)] public uint Metric;
    }
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint GetIpForwardTable2(ushort family, out IntPtr table);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern void FreeMibTable(IntPtr table);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint GetIfEntry2(IntPtr row);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint GetIfStackTable(out IntPtr table);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint GetIpInterfaceEntry(ref IpInterfaceRow row);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint SetIpInterfaceEntry(ref IpInterfaceRow row);
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32), DllImport("iphlpapi.dll")] private static extern uint GetBestRoute2(IntPtr luid, uint index, IntPtr source, ref SocketAddressNative destination, uint options, out RouteRow route, out SocketAddressNative bestSource);
}
