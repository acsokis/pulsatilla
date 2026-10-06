using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Pulsatilla.Wpf;

public sealed class NetworkPathPanel : UserControl, IDisposable
{
    private readonly ActiveInterfaceService _service;
    private readonly InterfacePriorityService _priority = new();
    private readonly TabControl _tabs = new();
    private readonly TextBlock _path = Text("Reading local Windows network state…");
    private readonly TextBlock _message = Text("");
    private readonly ComboBox _mode = new() { ItemsSource = new[] { "AUTO", "MANUAL" }, SelectedIndex = 0, MinWidth = 120 };
    private readonly ComboBox _adapters = new() { DisplayMemberPath = "Label", SelectedValuePath = "Id", MinWidth = 180 };
    private readonly DataGrid _routeGrid = NewGrid();
    private readonly DataGrid _metricGrid = NewGrid();
    private readonly TextBlock _diagnostics = Text("");
    private readonly TextBox _metricValue = new() { Text = "25", Width = 80, Margin = new Thickness(4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly Button _undo = Button("Undo Last Change");
    private string _adapterSignature = "";
    private string _routeSignature = "";
    private string _metricSignature = "";
    private bool _updating;
    private bool _disposed;
    public NetworkPathSnapshot? CurrentSnapshot => _service.CurrentSnapshot;
    public string? SelectedAdapterId => CurrentSnapshot?.SelectedAdapter?.Id;
    public void StartMonitoring() => _service.Start();
    public event EventHandler<NetworkPathChangedEventArgs>? PathChanged;
    public event EventHandler? InspectRequested;
    public Task RefreshAsync(CancellationToken token = default) => _service.RefreshAsync(token);
    public void SelectManualAdapter(string id)
    {
        _updating = true;
        try { _mode.SelectedIndex = 1; _adapters.SelectedValue = id; }
        finally { _updating = false; }
        _service.SetSelection(InterfaceSelectionMode.Manual, id);
    }

    public NetworkPathPanel(INetworkTopologyReader? topologyReader = null)
    {
        _service = new ActiveInterfaceService(topologyReader);
        Margin = new Thickness(10);
        SetResourceReference(ForegroundProperty, "Theme_D8E4DA");
        _tabs.SetResourceReference(Control.BackgroundProperty, "Theme_101B13");
        _tabs.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA");
        _metricValue.SetResourceReference(Control.BackgroundProperty, "Theme_101B13");
        _metricValue.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA");
        foreach (var combo in new[] { _mode, _adapters })
        { combo.SetResourceReference(Control.BackgroundProperty, "Theme_101B13"); combo.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA"); }
        var path = new StackPanel { Margin = new Thickness(10) };
        path.Children.Add(Text("ACTIVE NETWORK PATH", 20)); path.Children.Add(_path);
        var row = new WrapPanel { Margin = new Thickness(0, 12, 0, 12) };
        row.Children.Add(_mode); row.Children.Add(_adapters);
        var refresh = Button("Refresh Network State"); refresh.Click += async (_, _) => await _service.RefreshAsync(); row.Children.Add(refresh);
        var inspect = Button("Inspect Traffic"); inspect.Click += (_, _) => InspectRequested?.Invoke(this, EventArgs.Empty); row.Children.Add(inspect);
        path.Children.Add(row); path.Children.Add(Text("AUTO follows Windows' effective routed interface. MANUAL selects observation only; it does not change routing. IPv4 raw capture requires elevation and an IPv4 address."));
        AddTab("Network Path", Scroll(path));
        var adapterGrid = NewGrid(); adapterGrid.AutoGenerateColumns = false;
        var groupHeader = new FrameworkElementFactory(typeof(TextBlock));
        groupHeader.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        groupHeader.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        groupHeader.SetValue(TextBlock.MarginProperty, new Thickness(8));
        groupHeader.SetResourceReference(TextBlock.ForegroundProperty, "Theme_75DE88");
        adapterGrid.GroupStyle.Add(new GroupStyle { HeaderTemplate = new DataTemplate { VisualTree = groupHeader } });
        VirtualizingPanel.SetIsVirtualizingWhenGrouping(adapterGrid, true);
        foreach (var (heading, property) in new[] { ("Group", "Group"), ("Adapter", "Name"), ("Type", "Classification"), ("Status", "Status"), ("IPv4", "LocalIpv4"), ("Interface Index", "InterfaceIndex"), ("Windows Hardware", "HardwareInterface") })
            adapterGrid.Columns.Add(new DataGridTextColumn { Header = heading, Binding = new Binding(property), Width = DataGridLength.SizeToHeader });
        AddTab("Adapters", adapterGrid);
        _routeGrid.AutoGenerateColumns = false;
        foreach (var (heading, property) in new[] { ("Destination", "Prefix"), ("Family", "FamilyLabel"), ("Gateway", "Gateway"), ("Interface", "InterfaceName"), ("Route Metric", "RouteMetric"), ("Interface Metric", "InterfaceMetric"), ("Effective Metric", "EffectiveMetric"), ("Flags", "Flags") })
            _routeGrid.Columns.Add(new DataGridTextColumn { Header = heading, Binding = new Binding(property), Width = DataGridLength.SizeToHeader });
        AddTab("Routes", _routeGrid);
        var metrics = new Grid(); metrics.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); metrics.RowDefinitions.Add(new() { Height = GridLength.Auto });
        _metricGrid.AutoGenerateColumns = false;
        foreach (var (heading, property) in new[] { ("Adapter", "Adapter"), ("Type", "Type"), ("Index", "InterfaceIndex"), ("Family", "Family"), ("Automatic", "Automatic"), ("Metric", "Metric"), ("Default Route", "DefaultRoute"), ("Gateway", "Gateway"), ("Effective Metric", "EffectiveMetric") })
            _metricGrid.Columns.Add(new DataGridTextColumn { Header = heading, Binding = new Binding(property), Width = DataGridLength.SizeToHeader });
        metrics.Children.Add(_metricGrid);
        var actions = new StackPanel(); actions.Children.Add(Text("EXPLICIT WINDOWS SETTINGS CHANGE — select one family/interface. Previous state is recorded; readback is required. Administrator rights may be required."));
        var buttons = new WrapPanel(); buttons.Children.Add(_metricValue);
        var set = Button("Set Manual Metric"); set.Click += async (_, _) => await ChangeMetricAsync(false); buttons.Children.Add(set);
        var automatic = Button("Restore Automatic Metric"); automatic.Click += async (_, _) => await ChangeMetricAsync(true); buttons.Children.Add(automatic);
        _undo.IsEnabled = false; _undo.Click += async (_, _) =>
        {
            if (MessageBox.Show(Window.GetWindow(this), "Restore the recorded previous settings for the last changed interface?", "Undo Interface Priority", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _undo.IsEnabled = false; var result = await _priority.UndoAsync(); _message.Text = result.Message; _undo.IsEnabled = result.CanUndo; await _service.RefreshAsync();
        };
        buttons.Children.Add(_undo); actions.Children.Add(buttons); actions.Children.Add(_message);
        var reachableActions = Scroll(actions); reachableActions.MaxHeight = 230;
        metrics.SizeChanged += (_, _) => reachableActions.MaxHeight = Math.Max(24, Math.Min(230, metrics.ActualHeight * 0.55));
        Grid.SetRow(reachableActions, 1); metrics.Children.Add(reachableActions);
        AddTab("Interface Priority", metrics); AddTab("Diagnostics", Scroll(_diagnostics));
        Content = _tabs;
        _mode.SelectionChanged += (_, _) => { if (!_updating) _service.SetSelection(_mode.SelectedIndex == 0 ? InterfaceSelectionMode.Auto : InterfaceSelectionMode.Manual, _adapters.SelectedValue as string); };
        _adapters.SelectionChanged += (_, _) => { if (!_updating && _mode.SelectedIndex == 1) _service.SetSelection(InterfaceSelectionMode.Manual, _adapters.SelectedValue as string); };
        _service.PathChanged += (_, args) =>
        {
            if (_disposed || Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_disposed) return;
                Update(args.Snapshot, adapterGrid); PathChanged?.Invoke(this, args);
            });
        };
        Loaded += (_, _) => _service.Start();
    }
    public void SelectSection(string section) => _tabs.SelectedItem = _tabs.Items.Cast<TabItem>().FirstOrDefault(t => Equals(t.Header, section)) ?? _tabs.Items[0];
    private void Update(NetworkPathSnapshot snapshot, DataGrid adapterGrid)
    {
        _updating = true;
        try
        {
            var signature = string.Join('|', snapshot.Topology.Adapters.Select(a => $"{a.Id}:{a.Name}:{a.Group}:{a.Status}:{string.Join(',', a.Addresses)}"));
            if (_adapterSignature != signature)
            {
                _adapterSignature = signature;
                _adapters.ItemsSource = snapshot.Topology.Adapters.Where(a => a.Classification != AdapterClassification.Loopback)
                    .OrderBy(a => a.Group).ThenBy(a => a.Name).Select(a => new AdapterChoice(a.Id, $"[{a.Group}] {a.Name} — {a.Status}")).ToArray();
                var grouped = new CollectionViewSource { Source = snapshot.Topology.Adapters.OrderBy(a => a.Group).ThenBy(a => a.Name).ToArray() };
                grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NetworkAdapterInfo.Group)));
                adapterGrid.ItemsSource = grouped.View;
            }
            _adapters.SelectedValue = snapshot.SelectedAdapter?.Id;
            _path.Text = $"Effective IPv4: {snapshot.EffectiveIpv4Adapter?.Name ?? "Unknown"}\nEffective IPv6: {snapshot.EffectiveIpv6Adapter?.Name ?? "Unknown"}\nSelected observation interface: {snapshot.SelectedAdapter?.Name ?? "None"}\nPhysical underlay: {snapshot.UnderlayLabel}\nLayer: {(snapshot.SelectedAdapter?.Classification == AdapterClassification.LayeredTunnel ? snapshot.SelectedAdapter.Name : "None identified")}\nLocal IPv4: {snapshot.SelectedAdapter?.LocalIpv4 ?? "Unknown"}\nGateway: {string.Join(", ", snapshot.SelectedAdapter?.Gateways ?? [])}\n{snapshot.SelectionReason}\n{snapshot.Topology.Error}";
            var routeSignature = string.Join('|', snapshot.Topology.Routes.Select(r => r.ToString()));
            if (_routeSignature != routeSignature)
            {
                _routeSignature = routeSignature;
                var selectedRoute = _routeGrid.SelectedItem as NetworkRouteInfo;
                var routeRows = snapshot.Topology.Routes.OrderByDescending(r => r.IsDefault).ThenBy(r => r.EffectiveMetric).ToArray();
                _routeGrid.ItemsSource = routeRows;
                _routeGrid.SelectedItem = routeRows.FirstOrDefault(r => r.Prefix == selectedRoute?.Prefix && r.InterfaceIndex == selectedRoute.InterfaceIndex && r.Family == selectedRoute.Family);
            }
            var metricSelected = _metricGrid.SelectedItem as MetricRow;
            var rows = snapshot.Topology.Metrics.Select(m =>
            {
                var adapter = snapshot.Topology.Adapters.FirstOrDefault(a => (m.Family == AddressFamily.InterNetwork ? a.InterfaceIndex : a.Ipv6InterfaceIndex) == m.InterfaceIndex);
                var route = snapshot.Topology.Routes.Where(r => r.InterfaceIndex == m.InterfaceIndex && r.Family == m.Family && r.IsDefault).OrderBy(r => r.EffectiveMetric).FirstOrDefault();
                return new MetricRow(adapter?.Name ?? "Unknown", adapter?.Classification.ToString() ?? "Unknown", m.InterfaceIndex, m.Family, m.Automatic, m.Metric, route is not null, route?.Gateway ?? "—", route?.EffectiveMetric);
            }).ToArray();
            var metricSignature = string.Join('|', rows.Select(r => r.ToString()));
            if (_metricSignature != metricSignature)
            {
                _metricSignature = metricSignature;
                _metricGrid.ItemsSource = rows;
                _metricGrid.SelectedItem = rows.FirstOrDefault(r => r.InterfaceIndex == metricSelected?.InterfaceIndex && r.Family == metricSelected.Family);
            }
            _diagnostics.Text = $"LOCAL READ-ONLY DIAGNOSTICS\nActive IPv4 route: {snapshot.RouteLabel}\nIPv6 route: {snapshot.EffectiveIpv6Adapter?.Name ?? "Unknown"}\nUnderlay: {snapshot.UnderlayLabel}\nDNS servers: {string.Join(", ", snapshot.SelectedAdapter?.DnsServers ?? [])}\nIPv4 default routes: {snapshot.Topology.Routes.Count(r => r.IsDefault && r.Family == AddressFamily.InterNetwork)}\nIPv6 default routes: {snapshot.Topology.Routes.Count(r => r.IsDefault && r.Family == AddressFamily.InterNetworkV6)}\nObserved: {snapshot.Topology.ObservedAtUtc:O}\n\nGateway reachability, DNS resolution, latency and packet loss are active operations; use existing explicit diagnostics controls. Public IP is not queried automatically. A detected tunnel is not proof of anonymity or encryption.";
        }
        finally { _updating = false; }
    }
    private async Task ChangeMetricAsync(bool automatic)
    {
        if (_metricGrid.SelectedItem is not MetricRow row) { _message.Text = "Select an interface and address family first."; return; }
        if (!automatic && (!uint.TryParse(_metricValue.Text, out var metric) || metric is < 1 or > 9999)) { _message.Text = "Enter a manual metric from 1 to 9999."; return; }
        var value = automatic ? row.Metric : uint.Parse(_metricValue.Text);
        if (MessageBox.Show(Window.GetWindow(this), $"Change {row.Adapter}, {row.Family}, interface {row.InterfaceIndex} to {(automatic ? "automatic metric" : "manual metric " + value)}? This changes Windows routing preference and may interrupt connectivity.", "Confirm Interface Priority", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        IsEnabled = false;
        try { var result = await _priority.ApplyAsync(row.InterfaceIndex, row.Family, automatic, value); _message.Text = result.Message; _undo.IsEnabled = result.CanUndo; await _service.RefreshAsync(); }
        finally { IsEnabled = true; }
    }
    private void AddTab(string title, object content)
    {
        var tab = new TabItem { Header = title, Content = content };
        tab.SetResourceReference(Control.BackgroundProperty, "Theme_101B13"); tab.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA");
        _tabs.Items.Add(tab);
    }
    private static TextBlock Text(string value, double size = 13)
    { var text = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, FontSize = size, Margin = new Thickness(4) }; text.SetResourceReference(TextBlock.ForegroundProperty, "Theme_D8E4DA"); return text; }
    private static Button Button(string value)
    {
        var button = new Button { Content = value, Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(4), MinHeight = 32 };
        button.SetResourceReference(Control.BackgroundProperty, "Theme_101B13"); button.SetResourceReference(Control.ForegroundProperty, "Theme_D8E4DA");
        if (Application.Current?.TryFindResource("ActionButton") is Style style) button.Style = style;
        return button;
    }
    private static DataGrid NewGrid()
    {
        var grid = new DataGrid { IsReadOnly = true, CanUserAddRows = false, EnableRowVirtualization = true, EnableColumnVirtualization = true,
            SelectionMode = DataGridSelectionMode.Single, HeadersVisibility = DataGridHeadersVisibility.Column, Margin = new Thickness(4) };
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto); ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto); return grid;
    }
    private static ScrollViewer Scroll(UIElement element) => new() { Content = element, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
    public void Dispose() { _disposed = true; _service.Dispose(); }
    private sealed record AdapterChoice(string Id, string Label);
    private sealed record MetricRow(string Adapter, string Type, int InterfaceIndex, AddressFamily Family, bool Automatic, uint Metric, bool DefaultRoute, string Gateway, ulong? EffectiveMetric);
}
