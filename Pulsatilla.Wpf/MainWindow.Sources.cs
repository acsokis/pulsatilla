using System.Collections.ObjectModel;
using System.Net;
using System.Windows;
using System.Windows.Controls;

namespace Pulsatilla.Wpf;

public partial class MainWindow
{
    private readonly TrafficSourceTracker _sourceTracker = new();
    private readonly Dictionary<string, string> _sourceNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _resolvingSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _sourceResolutionSlots = new(2);
    private readonly CancellationTokenSource _sourceResolutionCancellation = new();
    private DateTime _lastSourceRefresh;
    public ObservableCollection<TrafficSourceNode> TopIpSources { get; } = [];
    public ObservableCollection<TrafficSourceNode> TopSoftwareSources { get; } = [];

    private void RefreshTopSources(bool force = false)
    {
        var now = DateTime.UtcNow;
        if (!force && now - _lastSourceRefresh < TimeSpan.FromMilliseconds(500)) return;
        _lastSourceRefresh = now;
        Update(TopIpSources, _sourceTracker.TopIps(), identify: true);
        Update(TopSoftwareSources, _sourceTracker.TopApplications(), identify: false);
        void Update(ObservableCollection<TrafficSourceNode> target, IReadOnlyList<TrafficSourceSnapshot> rows, bool identify)
        {
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index]; var node = target.FirstOrDefault(item => item.Key == row.Key);
                if (node is null) { node = new(row.Key); target.Insert(index, node); }
                else if (target.IndexOf(node) != index) target.Move(target.IndexOf(node), index);
                var identity = "";
                if (identify)
                {
                    if (row.Key == Ipv4Value.Text) identity = Environment.MachineName + " (this PC)";
                    else if (_sourceNames.TryGetValue(row.Key, out var resolved)) identity = resolved;
                    else if (_observedMacs.ContainsKey(row.Key)) identity = "Connected LAN device";
                }
                node.Update(row, rows.Max(value => identify ? (double)value.Packets : value.Bytes), identity, byBytes: !identify);
                if (identify && _observedMacs.TryGetValue(row.Key, out var mac)) node.Details.Insert(0, "Observed LAN MAC: " + mac);
                if (identify) node.Details.Add("Expand to resolve a DNS name. Program attribution describes this PC, not remote software.");
            }
            while (target.Count > rows.Count) target.RemoveAt(target.Count - 1);
        }
    }

    private async void TopSource_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem { DataContext: TrafficSourceNode node } ||
            !TopIpSources.Contains(node) || _sourceNames.ContainsKey(node.Key) ||
            !IPAddress.TryParse(node.Key, out var address) || node.Key == Ipv4Value.Text || !_resolvingSources.Add(node.Key)) return;
        var token = _sourceResolutionCancellation.Token;
        try
        {
            await _sourceResolutionSlots.WaitAsync(token);
            try
            {
                var name = (await Dns.GetHostEntryAsync(address).WaitAsync(TimeSpan.FromSeconds(2), token)).HostName;
                if (_sourceNames.Count >= 256) _sourceNames.Remove(_sourceNames.Keys.First());
                _sourceNames[node.Key] = name == node.Key ? "Name unavailable" : name;
            }
            finally { _sourceResolutionSlots.Release(); }
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or TimeoutException)
        {
            if (_sourceNames.Count >= 256) _sourceNames.Remove(_sourceNames.Keys.First());
            _sourceNames[node.Key] = "DNS name unavailable";
        }
        catch (OperationCanceledException) { }
        finally { _resolvingSources.Remove(node.Key); if (!token.IsCancellationRequested) RefreshTopSources(force: true); }
    }
}
