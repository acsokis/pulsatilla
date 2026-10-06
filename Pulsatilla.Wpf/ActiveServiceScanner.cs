using System.IO;
using System.Net;
using System.Net.Sockets;

namespace Pulsatilla.Wpf;

public enum ServiceBannerState { BannerReceived, NoBanner, DeadlineReached, PreviewLimitReached }
public sealed record ServiceBannerResult(string Address, int Port, byte[] Bytes, ServiceBannerState State, TimeSpan Elapsed);
public interface IServiceBannerConnection : IAsyncDisposable { Stream Stream { get; } }
public interface IServiceBannerTransport
{ ValueTask<IServiceBannerConnection> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken); }

/// <summary>Explicit active TCP connection and receive only: no protocol request, handshake payload or sample execution.</summary>
public sealed class ActiveServiceScanner
{
    public const int MaximumBannerBytes = 16 * 1024;
    private readonly IServiceBannerTransport _transport;
    private readonly TimeSpan _deadline;
    private readonly SemaphoreSlim _singleConnection = new(1, 1);
    public ActiveServiceScanner(IServiceBannerTransport? transport = null, TimeSpan? deadline = null)
    {
        _transport = transport ?? new TcpServiceBannerTransport();
        _deadline = deadline ?? TimeSpan.FromSeconds(5);
        if (_deadline <= TimeSpan.Zero || _deadline > TimeSpan.FromSeconds(5)) throw new ArgumentOutOfRangeException(nameof(deadline));
    }
    public static IPAddress ValidateTarget(string input, int port)
    {
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port), "A port from 1 to 65535 is required.");
        var value = input.Trim();
        if (value.Length > 100 || !(value.Contains(':') || value.Split('.').Length == 4) || !IPAddress.TryParse(value, out var address))
            throw new ArgumentException("Enter a literal IPv4 or IPv6 address; hostnames and ranges are not accepted.", nameof(input));
        if (!value.Contains(':') && value.Split('.').Any(p => !byte.TryParse(p, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out _) || p.Length > 1 && p[0] == '0'))
            throw new ArgumentException("IPv4 targets require four unambiguous decimal octets.", nameof(input));
        if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast) ||
            address.IsIPv6Multicast || address.AddressFamily == AddressFamily.InterNetwork && address.GetAddressBytes()[0] is >= 224 and <= 239)
            throw new ArgumentException("Unspecified, broadcast and multicast targets are not supported.", nameof(input));
        return address;
    }
    public async Task<ServiceBannerResult> InspectAsync(string input, int port, CancellationToken cancellationToken = default)
    {
        var address = ValidateTarget(input, port); cancellationToken.ThrowIfCancellationRequested();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); deadline.CancelAfter(_deadline);
        var acquired = false; var watch = System.Diagnostics.Stopwatch.StartNew();
        var bytes = new byte[MaximumBannerBytes]; var count = 0; ServiceBannerState state;
        try
        {
            await _singleConnection.WaitAsync(deadline.Token).ConfigureAwait(false); acquired = true;
            await using var connection = await _transport.ConnectAsync(address, port, deadline.Token).ConfigureAwait(false);
            while (count < bytes.Length)
            {
                var read = await connection.Stream.ReadAsync(bytes.AsMemory(count), deadline.Token).ConfigureAwait(false);
                if (read == 0) break; count += read;
            }
            state = count == bytes.Length ? ServiceBannerState.PreviewLimitReached : count == 0 ? ServiceBannerState.NoBanner : ServiceBannerState.BannerReceived;
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested(); state = ServiceBannerState.DeadlineReached;
        }
        finally { if (acquired) _singleConnection.Release(); }
        return new(address.ToString(), port, bytes[..count], state, watch.Elapsed);
    }
    private sealed class TcpServiceBannerTransport : IServiceBannerTransport
    {
        public async ValueTask<IServiceBannerConnection> ConnectAsync(IPAddress address, int port, CancellationToken cancellationToken)
        {
            var client = new TcpClient(address.AddressFamily);
            try { await client.ConnectAsync(address, port, cancellationToken).ConfigureAwait(false); return new TcpConnection(client); }
            catch { client.Dispose(); throw; }
        }
    }
    private sealed class TcpConnection(TcpClient client) : IServiceBannerConnection
    {
        public Stream Stream => client.GetStream();
        public ValueTask DisposeAsync() { client.Dispose(); return ValueTask.CompletedTask; }
    }
}
