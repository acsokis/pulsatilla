using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;

namespace Pulsatilla.Wpf;

public sealed class PacketCaptureService
{
    private const int ReceiveAllCode = unchecked((int)0x98000001);

    public async Task CaptureAsync(IPAddress localAddress, Action<PacketObservation> onPacket, CancellationToken cancellationToken)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.IP)
        {
            ReceiveBufferSize = 1024 * 1024
        };
        socket.Bind(new IPEndPoint(localAddress, 0));
        socket.IOControl((IOControlCode)ReceiveAllCode, BitConverter.GetBytes(1), null);

        var buffer = new byte[65_535];
        while (!cancellationToken.IsCancellationRequested)
        {
            var length = await socket.ReceiveAsync(buffer.AsMemory(), SocketFlags.None, cancellationToken).ConfigureAwait(false);
            if (TryParse(buffer.AsSpan(0, length), out var packet)) onPacket(packet);
        }
    }

    internal static bool TryParse(ReadOnlySpan<byte> data, out PacketObservation packet)
    {
        packet = default!;
        if (data.Length < 20 || (data[0] >> 4) != 4) return false;
        var headerLength = (data[0] & 0x0F) * 4;
        if (headerLength < 20 || data.Length < headerLength) return false;
        var totalLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(2, 2));
        if (totalLength < headerLength || totalLength > data.Length) return false;
        data = data[..totalLength];
        var initialFragment = (BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6, 2)) & 0x1FFF) == 0;

        var source = new IPAddress(data.Slice(12, 4));
        var destination = new IPAddress(data.Slice(16, 4));
        var protocolNumber = data[9];
        var protocol = protocolNumber switch { 6 => "TCP", 17 => "UDP", 1 => "ICMP", _ => $"IP/{protocolNumber}" };
        var sourcePort = 0;
        var destinationPort = 0;
        if (initialFragment && protocolNumber is 6 or 17 && data.Length >= headerLength + 4)
        {
            sourcePort = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(headerLength, 2));
            destinationPort = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(headerLength + 2, 2));
        }

        string? dnsName = null;
        string[] dnsAddresses = [];
        if (initialFragment && protocolNumber == 17 && (sourcePort == 53 || destinationPort == 53))
            (dnsName, dnsAddresses) = ParseDnsResponse(data, headerLength + 8);

        var previewLength = Math.Min(data.Length, 96);
        var hex = Convert.ToHexString(data[..previewLength]);
        packet = new PacketObservation(source.ToString(), destination.ToString(), protocol, data.Length,
            sourcePort, destinationPort, hex, dnsName, dnsAddresses)
        {
            TcpFlags = initialFragment && protocolNumber == 6 && data.Length >= headerLength + 20 ? data[headerLength + 13] : (byte)0
        };
        return true;
    }

    private static (string? Name, string[] Addresses) ParseDnsResponse(ReadOnlySpan<byte> packet, int offset)
    {
        if (packet.Length < offset + 12) return (null, []);
        var dns = packet[offset..];
        var flags = BinaryPrimitives.ReadUInt16BigEndian(dns.Slice(2, 2));
        if ((flags & 0x8000) == 0) return (null, []);
        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(dns.Slice(4, 2));
        var answerCount = BinaryPrimitives.ReadUInt16BigEndian(dns.Slice(6, 2));
        var cursor = 12;
        string? queryName = null;
        for (var index = 0; index < Math.Min(questionCount, (ushort)16); index++)
        {
            if (!ReadDnsName(dns, ref cursor, out var name) || cursor + 4 > dns.Length) return (null, []);
            queryName ??= name;
            cursor += 4;
        }

        var addresses = new List<string>();
        for (var index = 0; index < Math.Min(answerCount, (ushort)64); index++)
        {
            if (!SkipDnsName(dns, ref cursor) || cursor + 10 > dns.Length) break;
            var type = BinaryPrimitives.ReadUInt16BigEndian(dns.Slice(cursor, 2));
            var recordClass = BinaryPrimitives.ReadUInt16BigEndian(dns.Slice(cursor + 2, 2));
            var dataLength = BinaryPrimitives.ReadUInt16BigEndian(dns.Slice(cursor + 8, 2));
            cursor += 10;
            if (cursor + dataLength > dns.Length) break;
            if (type == 1 && recordClass == 1 && dataLength == 4)
                addresses.Add(new IPAddress(dns.Slice(cursor, 4)).ToString());
            cursor += dataLength;
        }
        return (queryName, addresses.ToArray());
    }

    private static bool ReadDnsName(ReadOnlySpan<byte> data, ref int cursor, out string name)
    {
        var labels = new List<string>();
        while (cursor < data.Length)
        {
            var length = data[cursor++];
            if (length == 0)
            {
                name = string.Join('.', labels);
                return true;
            }
            if ((length & 0xC0) != 0 || cursor + length > data.Length)
            {
                name = string.Empty;
                return false;
            }
            labels.Add(System.Text.Encoding.ASCII.GetString(data.Slice(cursor, length)));
            cursor += length;
        }
        name = string.Empty;
        return false;
    }

    private static bool SkipDnsName(ReadOnlySpan<byte> data, ref int cursor)
    {
        while (cursor < data.Length)
        {
            var length = data[cursor++];
            if (length == 0) return true;
            if ((length & 0xC0) == 0xC0)
            {
                if (cursor >= data.Length) return false;
                cursor++;
                return true;
            }
            if ((length & 0xC0) != 0 || cursor + length > data.Length) return false;
            cursor += length;
        }
        return false;
    }
}

public sealed record PacketObservation(string Source, string Destination, string Protocol, int Length,
    int SourcePort, int DestinationPort, string HexPreview, string? DnsName, string[] DnsAddresses)
{
    public byte TcpFlags { get; init; }
    public DateTime CapturedAtUtc { get; init; } = DateTime.UtcNow;
}
