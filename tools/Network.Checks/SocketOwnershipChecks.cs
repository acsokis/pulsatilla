using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Pulsatilla.Wpf;

internal static class SocketOwnershipChecks
{
    public static int Run()
    {
        var count = 0; void Check(bool value, string message) { count++; if (!value) throw new InvalidOperationException(message); }
        var one = new ProcessIdentity(101, "one", "C:\\one.exe"); var two = new ProcessIdentity(202, "two", "C:\\two.exe");
        OwnedSocketSnapshot Tcp(ProcessIdentity id, string remote, int port = 443) => new(id, "TCP", "192.0.2.2", 50000, remote, port, "Established", true);
        OwnedSocketSnapshot Udp(ProcessIdentity id, string local = "0.0.0.0") => new(id, "UDP", local, 50000, "", 0, "Bound UDP endpoint", false);
        PacketObservation Packet(string protocol, string remote = "198.51.100.1", int remotePort = 443, bool inbound = false) =>
            new(inbound ? remote : "192.0.2.2", inbound ? "192.0.2.2" : remote, protocol, 40, inbound ? remotePort : 50000, inbound ? 50000 : remotePort, "", null, []);
        var resolver = new ProcessConnectionResolver(); var local = IPAddress.Parse("192.0.2.2");
        resolver.RefreshFromSockets([Tcp(one, "198.51.100.1"), Tcp(two, "198.51.100.2")]);
        Check(resolver.Resolve(Packet("TCP"), local) == one, "TCP remote address was ignored");
        Check(resolver.Resolve(Packet("TCP", "198.51.100.2"), local) == two, "TCP tuple second owner failed");
        Check(resolver.Resolve(Packet("TCP", inbound: true), local) == one, "Inbound tuple direction failed");
        Check(resolver.Resolve(Packet("TCP", remotePort: 80), local) is null, "TCP remote port was ignored");
        Check(resolver.Resolve(Packet("TCP", "203.0.113.9"), local) is null, "Unobserved TCP remote attributed");
        Check(resolver.Resolve(Packet("TCP"), IPAddress.Parse("192.0.2.3")) is null, "Unrelated interface attributed");
        resolver.RefreshFromSockets([Tcp(one, "198.51.100.1"), Tcp(two, "198.51.100.1")]);
        Check(resolver.Resolve(Packet("TCP"), local) is null, "Conflicting exact TCP owners were guessed");
        resolver.RefreshFromSockets([Udp(one)]); Check(resolver.Resolve(Packet("UDP"), local) == one, "Unique UDP wildcard owner unresolved");
        resolver.RefreshFromSockets([Udp(one), Udp(two)]); Check(resolver.Resolve(Packet("UDP"), local) is null, "Shared wildcard UDP owner guessed");
        resolver.RefreshFromSockets([Udp(one, "192.0.2.2"), Udp(two)]); Check(resolver.Resolve(Packet("UDP"), local) is null, "Exact/wildcard UDP collision guessed");
        resolver.RefreshFromSockets([Udp(one, "192.0.2.2"), Udp(one)]); Check(resolver.Resolve(Packet("UDP"), local) == one, "Same-owner UDP bindings incorrectly ambiguous");
        resolver.RefreshFromSockets([Tcp(one, "198.51.100.1")], true); Check(resolver.Resolve(Packet("TCP"), local) is null, "Truncated snapshot guessed owner");
        resolver.RefreshFromSockets(Enumerable.Range(0, ProcessConnectionResolver.MaximumSockets + 10).Select(i => Tcp(one, "198.51.100.1", i % 65536)));
        Check(resolver.SnapshotTruncated && resolver.CurrentOwnedSockets.Count == ProcessConnectionResolver.MaximumSockets, "Socket snapshot was not bounded");
        var table = new byte[28]; BinaryPrimitives.WriteUInt32LittleEndian(table, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(4), 5);
        IPAddress.Parse("192.0.2.2").GetAddressBytes().CopyTo(table, 8);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(12), 50000);
        IPAddress.Parse("198.51.100.1").GetAddressBytes().CopyTo(table, 16);
        BinaryPrimitives.WriteUInt16BigEndian(table.AsSpan(20), 443);
        BinaryPrimitives.WriteUInt32LittleEndian(table.AsSpan(24), 101);
        var decoded = ProcessConnectionResolver.DecodeTable(table, true, AddressFamily.InterNetwork, _ => one, out var clipped);
        Check(!clipped && decoded.Single().LocalPort == 50000 && decoded.Single().RemotePort == 443 && decoded.Single().IsActiveConnection, "TCP native network byte order failed");
        Check(decoded.Single().LocalAddress == "192.0.2.2" && decoded.Single().RemoteAddress == "198.51.100.1", "TCP native address offsets failed");
        var table6 = new byte[60]; BinaryPrimitives.WriteUInt32LittleEndian(table6, 1);
        IPAddress.Parse("2001:db8::1").GetAddressBytes().CopyTo(table6, 4);
        BinaryPrimitives.WriteUInt16BigEndian(table6.AsSpan(24), 50001);
        IPAddress.Parse("2001:db8::2").GetAddressBytes().CopyTo(table6, 28);
        BinaryPrimitives.WriteUInt16BigEndian(table6.AsSpan(48), 8443);
        BinaryPrimitives.WriteUInt32LittleEndian(table6.AsSpan(52), 5); BinaryPrimitives.WriteUInt32LittleEndian(table6.AsSpan(56), 202);
        var decoded6 = ProcessConnectionResolver.DecodeTable(table6, true, AddressFamily.InterNetworkV6, _ => two, out clipped);
        Check(decoded6.Single().RemotePort == 8443 && decoded6.Single().LocalAddress == "2001:db8::1", "IPv6 row layout failed");
        resolver.RefreshFromSockets(decoded6);
        var packet6 = new PacketObservation("2001:db8::1", "2001:db8::2", "TCP", 80, 50001, 8443, "", null, []);
        Check(resolver.Resolve(packet6, IPAddress.Parse("2001:db8::1")) == two, "IPv6 exact tuple unresolved");
        Check(ProcessConnectionResolver.DecodeTable(table[..20], true, AddressFamily.InterNetwork, _ => one, out clipped).Length == 0 && clipped, "Truncated native row accepted");
        BinaryPrimitives.WriteUInt32LittleEndian(table, uint.MaxValue);
        Check(ProcessConnectionResolver.DecodeTable(table, true, AddressFamily.InterNetwork, _ => one, out clipped).Length == 0 && clipped, "Unbounded native row count accepted");
        Check(!ProcessConnectionResolver.NativeSizeAllowed(uint.MaxValue) && !ProcessConnectionResolver.NativeSizeAllowed(3), "Native buffer bounds missing");
        Console.WriteLine($"Socket owner checks passed: {count} synthetic assertions.");
        return count;
    }
}
