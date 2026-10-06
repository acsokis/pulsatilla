using System.Net;
using Pulsatilla.Wpf;

var checks = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); checks++; Console.WriteLine("PASS: " + name); }
foreach (var target in new[] { "example.com", "127.1", "10.0.0.0/24", "10.0.0.*", "0.0.0.0", "255.255.255.255", "224.0.0.1", "::", "ff02::1", "010.0.0.1", "0x7f.0.0.1" })
{
    var rejected = false; try { ActiveServiceScanner.ValidateTarget(target, 22); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "reject unsafe or ambiguous target " + target);
}
Check(ActiveServiceScanner.ValidateTarget("192.0.2.1", 1).ToString() == "192.0.2.1" &&
    ActiveServiceScanner.ValidateTarget("2001:db8::1", 65535).ToString() == "2001:db8::1", "literal IPv4/IPv6 and boundary ports validate without DNS");
foreach (var port in new[] { 0, -1, 65536 })
{ var rejected = false; try { ActiveServiceScanner.ValidateTarget("192.0.2.1", port); } catch (ArgumentOutOfRangeException) { rejected = true; } Check(rejected, "reject out-of-range port " + port); }
var banner = System.Text.Encoding.ASCII.GetBytes("SSH-2.0-synthetic-fixture\r\n");
var normalStream = new ReadOnlyFixtureStream(banner);
var normal = new FakeTransport(() => normalStream);
var result = await new ActiveServiceScanner(normal).InspectAsync("192.0.2.1", 22);
Check(result.State == ServiceBannerState.BannerReceived && result.Bytes.SequenceEqual(banner) && normal.Disposed == 1, "actual returned banner bytes retained and owned connection disposed");
Check(normalStream.WriteAttempts == 0, "no application payload written to transport");
var oversized = new FakeTransport(() => new ReadOnlyFixtureStream(new byte[65536]));
var bounded = await new ActiveServiceScanner(oversized).InspectAsync("192.0.2.1", 22);
Check(bounded.Bytes.Length == 16384 && bounded.State == ServiceBannerState.PreviewLimitReached && oversized.Disposed == 1, "oversized banner capped at16KiB and connection closed");
var empty = new FakeTransport(() => new ReadOnlyFixtureStream([]));
Check((await new ActiveServiceScanner(empty).InspectAsync("192.0.2.1", 22)).State == ServiceBannerState.NoBanner, "EOF without banner reported honestly");
var delayed = new FakeTransport(() => new DelayedFixtureStream());
var expired = await new ActiveServiceScanner(delayed, TimeSpan.FromMilliseconds(100)).InspectAsync("192.0.2.1", 22);
Check(expired.State == ServiceBannerState.DeadlineReached && expired.Bytes.Length == 0 && delayed.Disposed == 1, "deadline closes a silent owned connection");
var partial = new FakeTransport(() => new DelayedFixtureStream(banner));
var partialResult = await new ActiveServiceScanner(partial, TimeSpan.FromMilliseconds(100)).InspectAsync("192.0.2.1", 22);
Check(partialResult.State == ServiceBannerState.DeadlineReached && partialResult.Bytes.SequenceEqual(banner) && partial.Disposed == 1, "partial banner preserved when deadline closes connection");
var cancelledTransport = new FakeTransport(() => new DelayedFixtureStream());
using (var cancellation = new CancellationTokenSource(100))
{
    var cancelled = false; try { await new ActiveServiceScanner(cancelledTransport).InspectAsync("192.0.2.1", 22, cancellation.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && cancelledTransport.Disposed == 1, "explicit cancellation preserved and owned connection disposed");
}
var serialized = new FakeTransport(() => new DelayedFixtureStream()); var scanner = new ActiveServiceScanner(serialized, TimeSpan.FromMilliseconds(150));
await Task.WhenAll(scanner.InspectAsync("192.0.2.1", 22), scanner.InspectAsync("192.0.2.2", 22));
Check(serialized.PeakActive == 1 && serialized.Active == 0, "concurrent callers never create simultaneous service connections");
var invalid = new FakeTransport(() => new ReadOnlyFixtureStream(banner));
try { await new ActiveServiceScanner(invalid).InspectAsync("example.com", 22); } catch (ArgumentException) { }
Check(invalid.Connected == 0, "invalid target cannot reach the transport");
var recoveryTransport = new FailOnceTransport(banner); var recoveryScanner = new ActiveServiceScanner(recoveryTransport);
var failed = false; try { await recoveryScanner.InspectAsync("192.0.2.1", 22); } catch (IOException) { failed = true; }
Check(failed && (await recoveryScanner.InspectAsync("192.0.2.1", 22)).State == ServiceBannerState.BannerReceived, "failed connection releases the session gate for a later explicit retry");
using (var alreadyCancelled = new CancellationTokenSource())
{
    alreadyCancelled.Cancel(); var preCancelledTransport = new FakeTransport(() => new ReadOnlyFixtureStream(banner));
    try { await new ActiveServiceScanner(preCancelledTransport).InspectAsync("192.0.2.1", 22, alreadyCancelled.Token); } catch (OperationCanceledException) { }
    Check(preCancelledTransport.Connected == 0, "pre-cancelled operation never reaches transport");
}
Console.WriteLine($"Service banner checks: {checks} PASS. Injected streams only; no TCP connection, DNS lookup or external request performed.");

sealed class FakeTransport(Func<Stream> factory) : IServiceBannerTransport
{
    public int Connected, Disposed, Active, PeakActive;
    public ValueTask<IServiceBannerConnection> ConnectAsync(IPAddress address, int port, CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Connected++; Active++; PeakActive = Math.Max(PeakActive, Active);
        return ValueTask.FromResult<IServiceBannerConnection>(new Connection(this, factory()));
    }
    private sealed class Connection(FakeTransport owner, Stream stream) : IServiceBannerConnection
    {
        public Stream Stream => stream;
        public ValueTask DisposeAsync() { stream.Dispose(); owner.Disposed++; owner.Active--; return ValueTask.CompletedTask; }
    }
}
class ReadOnlyFixtureStream(byte[] bytes) : MemoryStream(bytes, writable: false)
{
    public int WriteAttempts { get; private set; }
    public override void Write(byte[] buffer, int offset, int count) { WriteAttempts++; throw new Exception("Unexpected banner payload write."); }
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) { WriteAttempts++; throw new Exception("Unexpected banner payload write."); }
}
sealed class FailOnceTransport(byte[] bytes) : IServiceBannerTransport
{
    private bool _failed;
    public ValueTask<IServiceBannerConnection> ConnectAsync(IPAddress address, int port, CancellationToken token)
    {
        if (!_failed) { _failed = true; throw new IOException("Synthetic refusal."); }
        return new FakeTransport(() => new ReadOnlyFixtureStream(bytes)).ConnectAsync(address, port, token);
    }
}
sealed class DelayedFixtureStream(byte[]? first = null) : ReadOnlyFixtureStream(first ?? [])
{
    private bool _delivered;
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
    {
        if (!_delivered && first is { Length: > 0 }) { _delivered = true; first.AsMemory().CopyTo(buffer); return first.Length; }
        await Task.Delay(Timeout.Infinite, token); return 0;
    }
}
