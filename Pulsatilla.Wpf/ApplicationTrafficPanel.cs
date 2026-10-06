using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Pulsatilla.Wpf;

public sealed class ApplicationPolicyRequestEventArgs(string executablePath, ApplicationTrust trust) : EventArgs
{
    public string ExecutablePath { get; } = executablePath;
    public ApplicationTrust Trust { get; } = trust;
}

/// <summary>Local owner-table endpoints and retained captured bytes are separate observations.</summary>
public sealed class ApplicationTrafficPanel : UserControl
{
    public ObservableCollection<ApplicationTrafficSnapshot> Applications { get; } = [];
    public event EventHandler<ApplicationPolicyRequestEventArgs>? PolicyRequested;
    private readonly DataGrid _grid = new() { IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4) };
    private readonly FileInspectionPanel _file = new();
    private readonly TabControl _tabs = new();
    public ApplicationTrafficPanel()
    {
        var root = new Grid { Margin = new Thickness(12) }; root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); root.RowDefinitions.Add(new RowDefinition());
        root.Children.Add(_status); Grid.SetRow(_tabs, 1); root.Children.Add(_tabs); Content = root;
        SetResourceReference(ForegroundProperty, "Theme_E6F2E8");
        var overview = new Grid(); overview.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); overview.RowDefinitions.Add(new RowDefinition());
        var actions = new WrapPanel(); overview.Children.Add(actions); Grid.SetRow(_grid, 1); overview.Children.Add(_grid);
        void Button(string label, RoutedEventHandler handler) { var button = new Button { Content = label, Margin = new Thickness(4) }; button.Click += handler; actions.Children.Add(button); }
        Button("Inspect selected executable", async (_, _) => { if (_grid.SelectedItem is ApplicationTrafficSnapshot selected && selected.ExecutablePath.Length > 0) { _tabs.SelectedIndex = 1; await _file.InspectPathAsync(selected.ExecutablePath); } });
        foreach (var (label, trust) in new[] { ("Monitor / remove block", ApplicationTrust.Monitor), ("Block application", ApplicationTrust.Blocked), ("Trust (advisory)", ApplicationTrust.Trusted) })
            Button(label, (_, _) => { if (_grid.SelectedItem is ApplicationTrafficSnapshot selected && selected.ExecutablePath.Length > 0) PolicyRequested?.Invoke(this, new(selected.ExecutablePath, trust)); });
        Button("Open file location", (_, _) =>
        {
            if (_grid.SelectedItem is not ApplicationTrafficSnapshot selected) return;
            try
            {
                var path = LocalFileInspector.ValidateLocalFile(selected.ExecutablePath);
                var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                var start = new System.Diagnostics.ProcessStartInfo(explorer) { UseShellExecute = false }; start.ArgumentList.Add(Path.GetDirectoryName(path)!); System.Diagnostics.Process.Start(start);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.ComponentModel.Win32Exception) { _status.Text = "File location could not be opened."; }
        });
        foreach (var (label, name, width) in new[] { ("Application", "Application", 170d), ("PID", "ProcessId", 65d), ("Established TCP", "EstablishedTcp", 120d), ("Bound UDP endpoints", "BoundUdpEndpoints", 150d), ("Captured flows", "ObservedFlows", 110d),
            ("Remote hosts", "RemoteHosts", 250d), ("Countries", "Countries", 100d), ("Download bytes", "DownloadBytes", 120d), ("Upload bytes", "UploadBytes", 120d),
            ("Policy (configured)", "Policy", 150d), ("Signature (last inspection)", "Signature", 180d), ("Publisher (inspection)", "Publisher", 180d), ("Local risk (inspection)", "Risk", 160d), ("Inspected at UTC", "InspectedAtUtc", 180d),
            ("First seen UTC", "FirstSeenUtc", 180d), ("Last seen UTC", "LastSeenUtc", 180d), ("Executable", "ExecutablePath", 300d), ("SHA-256", "Sha256", 470d) })
            _grid.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(name), Width = width });
        _grid.ItemsSource = Applications;
        _tabs.Items.Add(new TabItem { Header = "Applications / local sockets", Content = overview }); _tabs.Items.Add(new TabItem { Header = "Selected executable details", Content = _file });
        _status.Text = "Established TCP and bound UDP endpoints cover the latest retained Windows owner-table rows; bounded snapshots may be incomplete. Captured flows/bytes are separate observations; zero captured bytes does not mean no activity. Select an executable for explicit hash/signature; no automatic hashing or blocking.";
        _file.InspectionCompleted += inspection =>
        {
            foreach (var item in Applications.Where(a => a.ExecutablePath.Equals(inspection.Path, StringComparison.OrdinalIgnoreCase))) item.ApplyInspection(inspection);
        };
    }
    public void Refresh(IEnumerable<LiveTrafficRow> rows, IReadOnlyList<OwnedSocketSnapshot>? sockets = null)
    {
        Dispatcher.VerifyAccess();
        var summaries = Aggregate(rows, sockets);
        var byKey = Applications.ToDictionary(a => a.Key, StringComparer.OrdinalIgnoreCase);
        var current = summaries.Select(s => s.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = Applications.Count - 1; index >= 0; index--) if (!current.Contains(Applications[index].Key)) Applications.RemoveAt(index);
        foreach (var summary in summaries)
            if (byKey.TryGetValue(summary.Key, out var existing)) existing.Update(summary); else Applications.Add(summary);
    }
    public static IReadOnlyList<ApplicationTrafficSnapshot> Aggregate(IEnumerable<LiveTrafficRow> rows, IReadOnlyList<OwnedSocketSnapshot>? sockets = null)
    {
        var groups = new Dictionary<string, (ProcessIdentity Identity, List<LiveTrafficRow> Flows, List<OwnedSocketSnapshot> Sockets)>(StringComparer.OrdinalIgnoreCase);
        static string Key(ProcessIdentity identity) => identity.ExecutablePath + "\0" + identity.ProcessId + (identity.ExecutablePath.Length == 0 ? "\0" + identity.Name : "");
        var socketKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Current local endpoint owners take priority over retained historical capture rows at the group cap.
        foreach (var socket in (sockets ?? []).Take(8192))
        {
            if (socket.Identity is not { ProcessId: > 0 } identity || socket.Protocol is not "TCP" and not "UDP") continue;
            var key = Key(identity);
            var endpointKey = key + "\0" + socket.Protocol + "\0" + socket.LocalAddress + "\0" + socket.LocalPort + "\0" + (socket.Protocol == "TCP" ? socket.RemoteAddress + "\0" + socket.RemotePort : "");
            if (!socketKeys.Add(endpointKey)) continue;
            if (!groups.TryGetValue(key, out var group)) { if (groups.Count >= 256) continue; groups[key] = group = (identity, [], []); }
            group.Sockets.Add(socket);
        }
        var flowReferences = new HashSet<LiveTrafficRow>();
        foreach (var row in rows.Take(8192))
        {
            if (!flowReferences.Add(row)) continue;
            var identity = new ProcessIdentity(row.ProcessId, row.Application, row.ExecutablePath); var key = Key(identity);
            if (!groups.TryGetValue(key, out var group)) { if (groups.Count >= 256) continue; groups[key] = group = (identity, [], []); }
            group.Flows.Add(row);
        }
        var now = DateTime.UtcNow;
        return groups.Select(pair =>
        {
            var group = pair.Value; var identity = group.Identity; long download = 0, upload = 0;
            foreach (var flow in group.Flows) { download = Add(download, flow.DownloadBytes); upload = Add(upload, flow.UploadBytes); }
            var remotes = group.Flows.Select(r => r.RemoteAddress).Concat(group.Sockets.Where(s => s.Protocol == "TCP").Select(s => s.RemoteAddress))
                .Where(h => h.Length > 0 && h is not "0.0.0.0" and not "::").Distinct(StringComparer.OrdinalIgnoreCase).Take(12);
            return new ApplicationTrafficSnapshot(pair.Key, identity.Name, identity.ExecutablePath, identity.ProcessId, group.Flows.Count,
                string.Join(", ", remotes), group.Flows.Count == 0 ? "Unknown" : string.Join(", ", group.Flows.Select(r => r.Country).Distinct(StringComparer.OrdinalIgnoreCase).Take(12)), download, upload,
                group.Flows.Count == 0 ? now : group.Flows.Min(r => r.FirstSeenUtc), group.Flows.Count == 0 || group.Sockets.Count > 0 ? now : group.Flows.Max(r => r.LastSeenUtc),
                group.Flows.FirstOrDefault()?.Policy ?? "Not queried",
                group.Sockets.Count(s => s.Protocol == "TCP" && s.IsActiveConnection && s.Status.Equals("Established", StringComparison.OrdinalIgnoreCase)),
                group.Sockets.Count(s => s.Protocol == "UDP"));
        }).ToArray();
    }
    private static long Add(long current, long value) => value <= 0 ? current : current > long.MaxValue - value ? long.MaxValue : current + value;
}

public sealed class ApplicationTrafficSnapshot(string key, string application, string executablePath, int processId, int observedFlows,
    string remoteHosts, string countries, long downloadBytes, long uploadBytes, DateTime firstSeenUtc, DateTime lastSeenUtc, string policy,
    int establishedTcp = 0, int boundUdpEndpoints = 0) : INotifyPropertyChanged
{
    public string Key { get; } = key;
    public string Application { get; } = application;
    public string ExecutablePath { get; } = executablePath;
    public int ProcessId { get; } = processId;
    public int ObservedFlows { get; private set; } = observedFlows;
    public int EstablishedTcp { get; private set; } = establishedTcp;
    public int BoundUdpEndpoints { get; private set; } = boundUdpEndpoints;
    public string RemoteHosts { get; private set; } = remoteHosts;
    public string Countries { get; private set; } = countries;
    public long DownloadBytes { get; private set; } = downloadBytes;
    public long UploadBytes { get; private set; } = uploadBytes;
    public DateTime FirstSeenUtc { get; private set; } = firstSeenUtc;
    public DateTime LastSeenUtc { get; private set; } = lastSeenUtc;
    public string Policy { get; private set; } = policy;
    public string Signature { get; private set; } = "Not inspected";
    public string Publisher { get; private set; } = "Unknown";
    public string Sha256 { get; private set; } = "Not calculated";
    public string Risk { get; private set; } = "Unknown";
    public DateTime? InspectedAtUtc { get; private set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void Update(ApplicationTrafficSnapshot value)
    {
        ObservedFlows = value.ObservedFlows; EstablishedTcp = value.EstablishedTcp; BoundUdpEndpoints = value.BoundUdpEndpoints;
        RemoteHosts = value.RemoteHosts; Countries = value.Countries; DownloadBytes = value.DownloadBytes; UploadBytes = value.UploadBytes;
        FirstSeenUtc = FirstSeenUtc < value.FirstSeenUtc ? FirstSeenUtc : value.FirstSeenUtc; LastSeenUtc = value.LastSeenUtc; Policy = value.Policy;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
    internal void ApplyInspection(FileInspection inspection)
    {
        Signature = inspection.Signature.Status.ToString(); Publisher = inspection.Signature.Publisher; Sha256 = inspection.Sha256; Risk = inspection.Risk; InspectedAtUtc = DateTime.UtcNow;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}
