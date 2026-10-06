using System.Net;
using System.Net.Sockets;

namespace Pulsatilla.Wpf;

public enum InspectionState { Stopped, Starting, Inspecting, Stopping, Failed }
public sealed record InspectionSnapshot(InspectionState State, string? AdapterId, string? Address,
    DateTime? StartedAtUtc, long PacketsReceived, string Message)
{
    public bool IsActive => State is InspectionState.Starting or InspectionState.Inspecting or InspectionState.Stopping;
}
public sealed record InspectionOutcome(bool Started, bool Success, string Message);

/// <summary>One explicitly started IPv4 capture lifetime, shared by every passive consumer.</summary>
public sealed class TrafficInspectionService : IDisposable
{
    private readonly object _sync = new();
    private readonly Func<IPAddress, Action<PacketObservation>, Action, CancellationToken, Task> _capture;
    private CancellationTokenSource? _operation;
    private InspectionSnapshot _snapshot = new(InspectionState.Stopped, null, null, null, 0, "Inspection stopped.");
    private long _packets;
    private bool _disposed;
    public event EventHandler? StateChanged;
    public InspectionSnapshot Snapshot { get { lock (_sync) return _snapshot with { PacketsReceived = Interlocked.Read(ref _packets) }; } }
    public TrafficInspectionService(Func<IPAddress, Action<PacketObservation>, Action, CancellationToken, Task>? capture = null)
        => _capture = capture ?? ((address, packet, ready, token) => new PacketCaptureService().CaptureAsync(address, packet, token, ready));

    public async Task<InspectionOutcome> RunAsync(string adapterId, IPAddress address, Action<PacketObservation> onPacket)
    {
        if (address.AddressFamily != AddressFamily.InterNetwork || address.Equals(IPAddress.Any) || address.Equals(IPAddress.None))
            return new(false, false, "The current capture backend requires a usable IPv4 address.");
        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_disposed) return new(false, false, "Inspection service closed.");
            if (_operation is not null) return new(false, false, "An inspection session is already active or stopping.");
            operation = new(); _operation = operation; Interlocked.Exchange(ref _packets, 0);
            _snapshot = new(InspectionState.Starting, adapterId, address.ToString(), null, 0, "Opening native IPv4 raw capture; administrator access may be required.");
        }
        Notify();
        var succeeded = true;
        var message = "Inspection stopped; retained observations remain available.";
        try
        {
            await _capture(address, packet =>
            {
                lock (_sync)
                {
                    if (_disposed || !ReferenceEquals(_operation, operation) || operation.IsCancellationRequested
                        || _snapshot.State is InspectionState.Stopping or InspectionState.Stopped or InspectionState.Failed) return;
                    onPacket(packet);
                    Interlocked.Increment(ref _packets);
                }
            }, () =>
            {
                lock (_sync)
                {
                    if (_disposed || !ReferenceEquals(_operation, operation) || operation.IsCancellationRequested || _snapshot.State != InspectionState.Starting) return;
                    _snapshot = _snapshot with { State = InspectionState.Inspecting, StartedAtUtc = DateTime.UtcNow, Message = "INSPECTING — native IPv4 raw socket. IPv6/loopback coverage is not provided by this backend." };
                }
                Notify();
            }, operation.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException)
        {
            succeeded = false;
            message = error is SocketException { SocketErrorCode: SocketError.AccessDenied }
                ? "Raw capture access denied (10013). Restart as administrator to use this backend."
                : "Native capture failed or the selected interface became unavailable. Refresh Network Path before retrying.";
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_operation, operation))
                {
                    _snapshot = _snapshot with { State = succeeded ? InspectionState.Stopped : InspectionState.Failed, Message = message };
                    _operation = null;
                }
            }
            operation.Dispose(); Notify();
        }
        return new(true, succeeded, message);
    }

    public void Stop(string reason = "Stopping inspection…")
    {
        CancellationTokenSource operation;
        lock (_sync)
        {
            if (_operation is null) return;
            operation = _operation;
            _snapshot = _snapshot with { State = InspectionState.Stopping, Message = reason };
        }
        // A backend cancellation registration can call back into the service; never run it under our state lock.
        try { _ = ObserveCancellationAsync(operation.CancelAsync()); }
        catch (ObjectDisposedException) { } // The capture finished between unlocking and cancellation.
        catch (AggregateException) { } // All registrations were invoked; a throwing registration cannot strand cleanup.
        Notify();
    }
    private static async Task ObserveCancellationAsync(Task cancellation)
    {
        try { await cancellation.ConfigureAwait(false); }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
    }
    private void Notify()
    {
        var subscribers = StateChanged;
        if (subscribers is null) return;
        foreach (EventHandler handler in subscribers.GetInvocationList())
            try { handler(this, EventArgs.Empty); }
            catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) { }
    }
    public void Dispose() { lock (_sync) _disposed = true; Stop("Application closing; inspection cancelled."); }
}
