using System.Net.Sockets;

namespace Pulsatilla.Wpf;

public sealed record MetricChangeResult(bool Success, string Message, InterfaceMetricState? Current, bool CanUndo);
/// <summary>Explicit changes only. Never elevates, executes a shell, or modifies interfaces on construction.</summary>
public sealed class InterfacePriorityService(IInterfaceMetricBackend? backend = null)
{
    private readonly IInterfaceMetricBackend _backend = backend ?? new WindowsNetworkTopology();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private (InterfaceMetricState Before, InterfaceMetricState After)? _undo;
    public bool CanUndo => _undo is not null;
    public async Task<MetricChangeResult> ApplyAsync(int index, AddressFamily family, bool automatic, uint metric, CancellationToken token = default)
    {
        if (index <= 0 || family is not AddressFamily.InterNetwork and not AddressFamily.InterNetworkV6 || (!automatic && metric is < 1 or > 9999))
            throw new ArgumentOutOfRangeException(nameof(metric), "Manual metric must be 1–9999 and interface/family must be valid.");
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try { return await Task.Run(() => Change(index, family, automatic, metric), token).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private MetricChangeResult Change(int index, AddressFamily family, bool automatic, uint metric)
    {
        InterfaceMetricState? before = null;
        try
        {
            before = _backend.Read(index, family);
            _backend.Write(before with { Automatic = automatic, Metric = automatic ? before.Metric : metric });
            var after = _backend.Read(index, family);
            if (after.Automatic != automatic || (!automatic && after.Metric != metric)) throw new InvalidOperationException("Metric readback differs from requested state.");
            _undo = (before, after);
            return new(true, "Windows state read back and verified. Undo restores the recorded previous state.", after, true);
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
        {
            var message = "Change not verified. Administrator rights may be required; no automatic elevation was attempted.";
            if (before is not null)
            {
                try
                {
                    _backend.Write(before); var restored = _backend.Read(index, family);
                    if (restored.Automatic != before.Automatic || (!before.Automatic && restored.Metric != before.Metric)) throw new InvalidOperationException();
                    return new(false, message + " Previous state restored and verified.", restored, CanUndo);
                }
                catch (Exception rollback) when (rollback is System.ComponentModel.Win32Exception or InvalidOperationException or UnauthorizedAccessException)
                { message += " Previous state could not be verified; review Windows interface settings before continuing."; }
            }
            return new(false, message, null, CanUndo);
        }
    }
    public async Task<MetricChangeResult> UndoAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (_undo is not { } change) return new(false, "No successful metric change to undo.", null, false);
            return await Task.Run(() =>
            {
                try
                {
                    var current = _backend.Read(change.After.InterfaceIndex, change.After.Family);
                    if (current != change.After) return new MetricChangeResult(false, "Interface settings changed outside Pulsatilla; undo refused to overwrite them.", current, true);
                    _backend.Write(change.Before);
                    var restored = _backend.Read(change.Before.InterfaceIndex, change.Before.Family);
                    if (restored.Automatic != change.Before.Automatic || (!restored.Automatic && restored.Metric != change.Before.Metric))
                        return new(false, "Undo readback failed; review Windows settings.", restored, true);
                    _undo = null; return new(true, "Recorded previous state restored and verified.", restored, false);
                }
                catch (System.ComponentModel.Win32Exception) { return new(false, "Undo failed; administrator rights may be required.", null, true); }
            }, token).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }
}
