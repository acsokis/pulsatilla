using System.Net.NetworkInformation;

namespace Pulsatilla.Wpf;

public sealed class ActiveInterfaceService : IDisposable
{
    private readonly INetworkTopologyReader _reader;
    private readonly SemaphoreSlim _refresh = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _wake = new(0, 1);
    private Task? _loop;
    public NetworkPathSnapshot? CurrentSnapshot { get; private set; }
    public InterfaceSelectionMode Mode { get; private set; } = InterfaceSelectionMode.Auto;
    public string? ManualAdapterId { get; private set; }
    public event EventHandler<NetworkPathChangedEventArgs>? PathChanged;
    public ActiveInterfaceService(INetworkTopologyReader? reader = null) => _reader = reader ?? new WindowsNetworkTopology();
    public void Start()
    {
        if (_loop is not null) return;
        NetworkChange.NetworkAddressChanged += NetworkChanged;
        _loop = Task.Run(async () =>
        {
            while (!_stop.IsCancellationRequested)
            {
                await RefreshAsync(_stop.Token).ConfigureAwait(false);
                try { await _wake.WaitAsync(TimeSpan.FromSeconds(3), _stop.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        });
    }
    private void NetworkChanged(object? sender, EventArgs e) { if (_wake.CurrentCount == 0) try { _wake.Release(); } catch (SemaphoreFullException) { } }
    public void SetSelection(InterfaceSelectionMode mode, string? id = null)
    {
        Mode = mode; ManualAdapterId = mode == InterfaceSelectionMode.Manual ? id : null;
        if (CurrentSnapshot is { } current) Publish(NetworkPathSelector.Select(current.Topology, Mode, ManualAdapterId));
        NetworkChanged(null, EventArgs.Empty);
    }
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (!await _refresh.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;
        try
        {
            var topology = await Task.Run(_reader.Read, cancellationToken).ConfigureAwait(false);
            if (_stop.IsCancellationRequested || cancellationToken.IsCancellationRequested) return;
            Publish(NetworkPathSelector.Select(topology, Mode, ManualAdapterId));
        }
        catch (OperationCanceledException) { }
        catch (Exception e) when (e is NetworkInformationException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            var empty = new NetworkTopologySnapshot([], [], [], null, null, [], "Network snapshot unavailable; refresh again.", DateTime.UtcNow);
            Publish(NetworkPathSelector.Select(empty, Mode, ManualAdapterId));
        }
        finally { _refresh.Release(); }
    }
    private void Publish(NetworkPathSnapshot snapshot)
    {
        CurrentSnapshot = snapshot;
        PathChanged?.Invoke(this, new(snapshot));
    }
    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= NetworkChanged;
        _stop.Cancel();
        // An in-flight bounded native read is allowed to finish; it cannot publish after cancellation.
    }
}
