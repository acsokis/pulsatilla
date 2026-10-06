using System.ComponentModel;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Pulsatilla.Wpf;
using System.Buffers.Binary;

var assertions = 0;
assertions += SocketOwnershipChecks.Run();
void Check(bool value, string reason) { assertions++; if (!value) throw new InvalidOperationException(reason); }
NetworkAdapterInfo Adapter(string id, int index, AdapterClassification kind, OperationalStatus status = OperationalStatus.Up) =>
    new(id, id, id, index, index, status, NetworkInterfaceType.Ethernet, kind, ["192.0.2." + index], ["192.0.2.1"], [], kind == AdapterClassification.Physical, 0);
var ethernet = Adapter("ethernet", 10, AdapterClassification.Physical);
var wifi = Adapter("wifi", 20, AdapterClassification.Physical);
var tunnel = Adapter("tunnel", 30, AdapterClassification.LayeredTunnel);
var virtualNic = Adapter("virtual", 40, AdapterClassification.Virtual);
NetworkTopologySnapshot Snapshot(int? v4, int? v6 = null, params (int Higher, int Lower)[] layers) =>
    new([virtualNic, wifi, ethernet, tunnel], [], [], v4, v6, layers, null, DateTime.UtcNow);
Check(AdapterClassifier.Classify(NetworkInterfaceType.Ethernet, true, 0, "OpenVPN named physical card") == AdapterClassification.Physical, "Name overrode native hardware flag");
Check(AdapterClassifier.Classify(NetworkInterfaceType.Ethernet, null, 0, "OpenVPN") == AdapterClassification.Unknown, "Name-only tunnel classification");
Check(AdapterClassifier.Classify(NetworkInterfaceType.Ethernet, false, 0, "TAP-Windows Adapter") == AdapterClassification.LayeredTunnel, "Native non-hardware TAP evidence missing");
Check(AdapterClassifier.Classify(NetworkInterfaceType.Tunnel, null, 0, "x") == AdapterClassification.LayeredTunnel, "Native tunnel type ignored");
Check(AdapterClassifier.Classify(NetworkInterfaceType.Loopback, false, 0, "x") == AdapterClassification.Loopback, "Loopback type ignored");
Check(AdapterClassifier.Classify(NetworkInterfaceType.Ethernet, false, 0, "Hyper-V") == AdapterClassification.Virtual, "Virtual class missing");
Check(NetworkPathSelector.Select(Snapshot(10), InterfaceSelectionMode.Auto, null).SelectedAdapter == ethernet, "Enumeration order won over route");
var vpn = NetworkPathSelector.Select(Snapshot(30, null, (30, 20)), InterfaceSelectionMode.Auto, null);
Check(vpn.SelectedAdapter == tunnel && vpn.PhysicalUnderlay == wifi, "Effective VPN route / underlay selection failed");
Check(NetworkPathSelector.Select(Snapshot(30), InterfaceSelectionMode.Auto, null).PhysicalUnderlay is null, "Fabricated VPN underlay");
Check(NetworkPathSelector.Select(Snapshot(30, null, (30, 10), (30, 20)), InterfaceSelectionMode.Auto, null).PhysicalUnderlay is null, "Ambiguous underlay claimed as known");
Check(NetworkPathSelector.Select(Snapshot(30, null, (30, 40), (40, 30)), InterfaceSelectionMode.Auto, null).PhysicalUnderlay is null, "Cyclic relationship was not bounded");
Check(NetworkPathSelector.Select(Snapshot(30), InterfaceSelectionMode.Manual, "ethernet").SelectedAdapter == ethernet, "Manual mode ignored");
Check(NetworkPathSelector.Select(Snapshot(30), InterfaceSelectionMode.Manual, "removed").SelectedAdapter == tunnel, "Removed manual interface did not fall back");
Check(NetworkPathSelector.Select(Snapshot(null, 20), InterfaceSelectionMode.Auto, null).SelectedAdapter == wifi, "IPv6-only route selection failed");
var down = Snapshot(10) with { Adapters = [ethernet with { Status = OperationalStatus.Down }, wifi] };
Check(NetworkPathSelector.Select(down, InterfaceSelectionMode.Auto, null).SelectedAdapter == wifi, "Disconnected routed interface selected");
var route = new NetworkRouteInfo("0.0.0.0", 0, "192.0.2.1", 10, AddressFamily.InterNetwork, uint.MaxValue, uint.MaxValue, true, false, "Ethernet");
Check(route.EffectiveMetric == 8589934590UL, "Effective metric overflow");
Check((route with { InterfaceMetric = null }).EffectiveMetric is null, "Missing interface metric reported as zero");
Check(Marshal.SizeOf<WindowsNetworkTopology.RouteRow>() == 104 && Marshal.SizeOf<WindowsNetworkTopology.IpInterfaceRow>() == 168, "Native ABI size mismatch");
foreach (var ip in new[] { "1.1.1.1", "2001:db8::abcd", "fe80::1%12" })
    Check(WindowsNetworkTopology.SocketAddressNative.FromAddress(IPAddress.Parse(ip)).ToAddress().Equals(IPAddress.Parse(ip)), "Native address conversion failed");
var backend = new FakeMetricBackend(); var priority = new InterfacePriorityService(backend);
var result = await priority.ApplyAsync(10, AddressFamily.InterNetwork, false, 10);
Check(result.Success && result.CanUndo && backend.State.Metric == 10 && !backend.State.Automatic, "Metric state change not verified");
result = await priority.UndoAsync(); Check(result.Success && backend.State.Automatic && backend.State.Metric == 25 && !priority.CanUndo, "Recorded previous state not restored");
await priority.ApplyAsync(10, AddressFamily.InterNetwork, false, 10); backend.State = backend.State with { Metric = 77 };
Check(!(await priority.UndoAsync()).Success && backend.State.Metric == 77, "Undo overwrote outside settings change");
backend.State = backend.State with { Automatic = true, Metric = 25 }; backend.FailNextWrite = true;
Check(!(await priority.ApplyAsync(10, AddressFamily.InterNetwork, false, 11)).Success && backend.State.Automatic && backend.State.Metric == 25, "Failed change updated claimed state");
backend.IgnoreNextWrite = true;
Check(!(await priority.ApplyAsync(10, AddressFamily.InterNetwork, false, 12)).Success && backend.State.Automatic, "Readback mismatch was accepted");
try { await priority.ApplyAsync(10, AddressFamily.InterNetwork, false, 0); throw new Exception("Invalid metric accepted"); } catch (ArgumentOutOfRangeException) { assertions++; }
var reader = new FakeReader(Snapshot(30));
using (var service = new ActiveInterfaceService(reader))
{
    await service.RefreshAsync(); Check(service.CurrentSnapshot?.SelectedAdapter == tunnel, "Central service did not select routed interface");
    service.SetSelection(InterfaceSelectionMode.Manual, "wifi"); Check(service.CurrentSnapshot?.SelectedAdapter == wifi, "Central service manual selection failed");
    reader.Snapshot = Snapshot(10) with { Adapters = [ethernet, tunnel] };
    await service.RefreshAsync(); Check(service.CurrentSnapshot?.SelectedAdapter == ethernet, "Removed manual interface was retained by service");
    service.SetSelection(InterfaceSelectionMode.Auto); reader.Snapshot = Snapshot(30);
    await service.RefreshAsync(); Check(service.CurrentSnapshot?.SelectedAdapter == tunnel, "Route-change refresh failed");
}
if (args.Contains("--native-read-only"))
{
    var native = new WindowsNetworkTopology().Read();
    Check(native.Adapters.Length > 0, "Native adapter enumeration failed");
    Check(native.Routes.Any(r => r.Family == AddressFamily.InterNetwork), "Native IPv4 route ABI not validated");
    Check(native.Metrics.Any(m => m.Metric > 0 && m.Metric < 1_000_000), "Native interface metric ABI not validated");
    Check(native.Adapters.Any(a => a.HardwareInterface.HasValue), "Native hardware flags unavailable");
    Console.WriteLine("Local read-only IP Helper ABI checks passed. No addresses, adapter names, or traffic emitted.");
}
Console.WriteLine($"Network checks passed: {assertions} assertions; default checks are synthetic and make no network/settings changes.");

sealed class FakeReader(NetworkTopologySnapshot snapshot) : INetworkTopologyReader
{ public NetworkTopologySnapshot Snapshot = snapshot; public NetworkTopologySnapshot Read() => Snapshot; }
sealed class FakeMetricBackend : IInterfaceMetricBackend
{
    public InterfaceMetricState State = new(10, AddressFamily.InterNetwork, true, 25);
    public bool FailNextWrite;
    public bool IgnoreNextWrite;
    public InterfaceMetricState Read(int index, AddressFamily family) => State;
    public void Write(InterfaceMetricState state)
    {
        if (FailNextWrite) { FailNextWrite = false; throw new Win32Exception(5); }
        if (IgnoreNextWrite) { IgnoreNextWrite = false; return; }
        State = state;
    }
}
