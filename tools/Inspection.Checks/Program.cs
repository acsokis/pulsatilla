using System.Net;
using Pulsatilla.Wpf;

var assertions = 0;
void Check(bool value, string message) { assertions++; if (!value) throw new InvalidOperationException(message); }
var address = IPAddress.Parse("192.0.2.2");
var packet = new PacketObservation("192.0.2.2", "198.51.100.1", "TCP", 40, 50000, 443, "", null, []);
using (var fake = new FakeCapture())
using (var service = new TrafficInspectionService(fake.Run))
{
    var notified = 0;
    service.StateChanged += (_, _) => throw new ArgumentException("Subscriber failure must not strand capture");
    service.StateChanged += (_, _) => notified++;
    var received = 0;
    var first = service.RunAsync("one", address, _ => received++);
    await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(service.Snapshot.State == InspectionState.Starting && notified > 0, "Starting notification blocked on subscriber");
    var second = await service.RunAsync("two", address, _ => throw new Exception("Second session must not run"));
    Check(!second.Started && fake.Starts == 1, "Concurrent start opened more than one capture");
    fake.Ready!(); var started = service.Snapshot.StartedAtUtc;
    fake.Ready!(); Check(service.Snapshot.State == InspectionState.Inspecting && service.Snapshot.StartedAtUtc == started, "Repeated ready reset session time");
    fake.Packet!(packet); Check(received == 1 && service.Snapshot.PacketsReceived == 1, "Packet callback count failed");
    var oldPacket = fake.Packet; var oldReady = fake.Ready;
    service.Stop(); fake.Packet(packet); Check(received == 1, "Stopping session accepted packet");
    Check((await first.WaitAsync(TimeSpan.FromSeconds(3))).Success && service.Snapshot.State == InspectionState.Stopped, "Stop did not finish capture");
    fake.Reset(); var third = service.RunAsync("three", address, _ => received++);
    await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    oldReady!(); oldPacket!(packet);
    Check(service.Snapshot.State == InspectionState.Starting && service.Snapshot.PacketsReceived == 0 && received == 1, "Prior-generation callback corrupted new session");
    fake.Ready!(); fake.Packet!(packet);
    Check(service.Snapshot.PacketsReceived == 1 && received == 2, "Restarted session callback failed");
    service.Dispose(); Check((await third.WaitAsync(TimeSpan.FromSeconds(3))).Success, "Dispose did not cancel active session");
    Check(!(await service.RunAsync("closed", address, _ => { })).Started, "Disposed service allowed capture restart");
}
using (var service = new TrafficInspectionService((_, _, _, _) => throw new ArgumentException("Unexpected backend failure")))
{
    var failed = await service.RunAsync("one", address, _ => { });
    Check(!failed.Success && service.Snapshot.State == InspectionState.Failed, "Unexpected backend failure was mislabeled success");
}
using (var service = new TrafficInspectionService((_, _, ready, _) => { ready(); return Task.CompletedTask; }))
{
    Check(!(await service.RunAsync("one", IPAddress.IPv6Loopback, _ => { })).Started, "Unsupported IPv6 capture started");
    Check(!(await service.RunAsync("one", IPAddress.Any, _ => { })).Started, "Wildcard capture started");
    Check((await service.RunAsync("one", address, _ => { })).Success, "Immediate completion stranded capture");
}
TrafficInspectionService? stopping = null;
var cancelObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
using (stopping = new TrafficInspectionService(async (captureAddress, onPacket, ready, token) =>
{
    using var registration = token.Register(() =>
    {
        // This access takes the state lock: cancellation callbacks must not block behind a synchronous Stop caller holding it.
        _ = stopping!.Snapshot; cancelObserved.TrySetResult(); throw new ArgumentException("Throwing cancellation callback");
    });
    ready(); onPacket(packet); await Task.Delay(Timeout.Infinite, token);
}))
{
    var task = stopping.RunAsync("one", address, _ => stopping.Stop());
    await cancelObserved.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check((await task.WaitAsync(TimeSpan.FromSeconds(3))).Success && stopping.Snapshot.State == InspectionState.Stopped, "Reentrant stop / throwing cancellation callback deadlocked cleanup");
}
using (var service = new TrafficInspectionService((_, onPacket, ready, _) => { ready(); onPacket(packet); return Task.CompletedTask; }))
{
    var failed = await service.RunAsync("one", address, _ => throw new ArgumentException("Packet consumer failure"));
    Check(!failed.Success && service.Snapshot.State == InspectionState.Failed, "Packet consumer failure was mislabeled success");
}
using (var fake = new FakeCapture())
using (var service = new TrafficInspectionService(fake.Run))
{
    var invoked = 0; var allInvoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var requests = Enumerable.Range(0, 32).Select(i => Task.Run(async () =>
    {
        var request = service.RunAsync("parallel-" + i, address, _ => { });
        if (Interlocked.Increment(ref invoked) == 32) allInvoked.TrySetResult();
        return await request;
    })).ToArray();
    await allInvoked.Task.WaitAsync(TimeSpan.FromSeconds(3));
    Check(fake.Starts == 1, "Parallel requests opened multiple backends");
    service.Stop(); var outcomes = await Task.WhenAll(requests).WaitAsync(TimeSpan.FromSeconds(3));
    Check(outcomes.Count(o => o.Started) == 1 && service.Snapshot.State == InspectionState.Stopped, "Parallel request rejection/stop cleanup failed");
}
Console.WriteLine($"Inspection lifecycle checks passed: {assertions} synthetic assertions; no sockets, network traffic or OS settings changes.");

sealed class FakeCapture : IDisposable
{
    public int Starts;
    public Action<PacketObservation>? Packet;
    public Action? Ready;
    public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public async Task Run(IPAddress address, Action<PacketObservation> packet, Action ready, CancellationToken token)
    { Starts++; Packet = packet; Ready = ready; Entered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }
    public void Reset() => Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Dispose() { }
}
