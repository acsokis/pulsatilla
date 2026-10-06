using System.Windows;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Net;
using System.Windows.Controls;
using System.Windows.Data;

namespace Pulsatilla.Wpf;

public partial class MainWindow
{
    private WorkflowNavigation? _workflow;
    private NetworkPathPanel? _networkPath;
    private readonly TrafficInspectionService _inspection = new();
    private bool _applyingRoutedAdapter;
    private ApplicationTrafficPanel? _applicationPanel;
    private EmailAttachmentsPanel? _attachmentsPanel;
    private FileInspectionPanel? _samplePanel;
    private HexViewer? _packetHex;
    private ServiceBannerPanel? _servicePanel;
    private readonly TextBox _applicationFilter = new() { Width = 120, MaxLength = 256 };
    private readonly TextBox _hostFilter = new() { Width = 140, MaxLength = 256 };
    private readonly TextBox _countryFilter = new() { Width = 100, MaxLength = 80 };
    private readonly TextBox _portFilter = new() { Width = 75, MaxLength = 5 };
    private readonly ComboBox _policyFilter = new() { ItemsSource = new[] { "All", "Monitor", "Trusted", "Blocked" }, SelectedIndex = 0, Width = 105 };
    private readonly Dictionary<string, string> _passiveHostnames = new(StringComparer.OrdinalIgnoreCase);
    private void InitializeWorkflow()
    {
        _workflow = new WorkflowNavigation(this, MainTabs, _editionCapabilities);
        _workflow.InspectButton.Click += StartCaptureButton_Click;
        _workflow.StopButton.Click += StopCaptureButton_Click;
        _networkPath = new NetworkPathPanel();
        Loaded += (_, _) => _networkPath?.StartMonitoring();
        _workflow.InstallNetworkPanel(_networkPath);
        _networkPath.PathChanged += OnNetworkPathChanged;
        _networkPath.InspectRequested += (_, _) => StartCaptureButton_Click(this, new RoutedEventArgs());
        _inspection.StateChanged += (_, _) =>
        {
            if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(UpdateInspectionStatus);
        };
        _sampleTimer.Tick += (_, _) => UpdateInspectionStatus();
        UpdateInspectionStatus();
        PingStatusLabel.Text = "Latency diagnostics are stopped. Set a target explicitly to start; no Internet ping runs by default.";
        _applicationPanel = new ApplicationTrafficPanel();
        _workflow.ApplicationsHost.Content = _applicationPanel;
        _applicationPanel.PolicyRequested += async (_, request) =>
        {
            PolicyPathInput.Text = request.ExecutablePath;
            await SetApplicationPolicyAsync(request.ExecutablePath, request.Trust);
        };
        _connectionsTimer.Tick += (_, _) => _applicationPanel.Refresh(LiveTrafficRows, _processOwners.CurrentOwnedSockets);
        _attachmentsPanel = new EmailAttachmentsPanel();
        _workflow.AttachmentsHost.Content = _attachmentsPanel;
        _samplePanel = new FileInspectionPanel();
        _workflow.SampleHost.Content = _samplePanel;
        _servicePanel = new ServiceBannerPanel();
        _workflow.ServiceHost.Content = _servicePanel;
        TargetInput.TextChanged += (_, _) => _servicePanel.SetTarget(TargetInput.Text.Trim());
        _packetHex = new HexViewer { MinHeight = 150, MaxHeight = 280 };
        if (HexInspector.Parent is Panel packetPanel)
        {
            HexInspector.Visibility = Visibility.Collapsed;
            packetPanel.Children.Insert(packetPanel.Children.IndexOf(HexInspector) + 1, _packetHex);
        }
        ConfigureLiveTrafficDetails();
        ResponsiveWorkflowShell.Apply(this, MainTabs);
        InitializeEmailWorkflow();
    }

    private void OnNetworkPathChanged(object? sender, NetworkPathChangedEventArgs e)
    {
        if (_workflow is null) return;
        var path = e.Snapshot;
        _workflow.PathSummary.Text = $"Selected: {path.SelectedAdapter?.Name ?? "None"}\nPhysical: {path.UnderlayLabel}\nLayer: {(path.SelectedAdapter?.Classification == AdapterClassification.LayeredTunnel ? path.SelectedAdapter.Name : "None identified")}\nIPv4 route: {path.RouteLabel}\nLocal IPv4: {path.SelectedAdapter?.LocalIpv4 ?? "Unknown"}\nGateway: {string.Join(", ", path.SelectedAdapter?.Gateways ?? [])}\n{path.SelectionReason}";
        if (_inspection.Snapshot.IsActive && (_inspection.Snapshot.AdapterId != path.SelectedAdapter?.Id || _inspection.Snapshot.Address != path.SelectedAdapter?.LocalIpv4))
            _inspection.Stop("Routed interface/address changed or disappeared; restart inspection explicitly.");
        if (path.SelectedAdapter?.Id == _selectedAdapter?.Id && Ipv4Value.Text == path.SelectedAdapter?.LocalIpv4) return;
        if (_inspection.Snapshot.IsActive)
        {
            _inspection.Stop("Interface changed or disappeared; inspection stopped. Start again explicitly on the selected path.");
            while (_pendingPackets.TryDequeue(out _)) { }
        }
        _applyingRoutedAdapter = true;
        try
        {
            LoadAdapterInventory();
            var item = AdapterList.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag is NetworkInterface adapter && adapter.Id == path.SelectedAdapter?.Id);
            AdapterList.SelectedItem = item;
            if (item is null)
            {
                _selectedAdapter = null; Ipv4Value.Text = GatewayValue.Text = "Unknown";
                SelectedAdapterLabel.Text = "No operational observation interface";
            }
        }
        finally { _applyingRoutedAdapter = false; }
    }

    private void UpdateInspectionStatus()
    {
        if (_workflow is null) return;
        var state = _inspection.Snapshot;
        var active = state.IsActive;
        StartCaptureButton.Content = "Inspect Traffic";
        StopCaptureButton.Content = "Stop Inspection";
        StartCaptureButton.IsEnabled = _workflow.InspectButton.IsEnabled = !active && _selectedAdapter is not null;
        StopCaptureButton.IsEnabled = _workflow.StopButton.IsEnabled = active && state.State != InspectionState.Stopping;
        var duration = state.StartedAtUtc is { } started ? DateTime.UtcNow - started : TimeSpan.Zero;
        _workflow.InspectionSummary.Text = $"{state.State} — {state.Address ?? "No selected capture"} | Session packets {state.PacketsReceived:N0} | Retained flows {LiveTrafficRows.Count:N0} | Apps {LiveTrafficRows.Select(r => r.ExecutablePath).Where(p => p.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count()} | Alerts {_alertHistory.Count} | {duration:hh\\:mm\\:ss}\n{state.Message}";
        _workflow.SecuritySummary.Text = $"Inspection: {state.State} | Captured flows: {LiveTrafficRows.Count:N0} | Network alerts: {_alertHistory.Count} | Application rules: {_protection.Settings.Applications.Count}\nLocal findings are review indicators; signature and pattern matches are not malware verdicts.";
    }

    private IPAddress? GetSelectedIpv4()
    {
        try { return _selectedAdapter?.GetIPProperties().UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address; }
        catch (NetworkInformationException) { _inspection.Stop("Selected adapter unavailable; refresh Network Path."); return null; }
    }

    private static NetworkInterface[] EnumerateAdaptersSafely()
    {
        try { return NetworkInterface.GetAllNetworkInterfaces().Take(512).ToArray(); }
        catch (NetworkInformationException) { return []; }
    }

    private static IPInterfaceProperties? GetAdapterPropertiesSafely(NetworkInterface adapter)
    {
        try { return adapter.GetIPProperties(); }
        catch (NetworkInformationException) { return null; }
    }

    private void ConfigureLiveTrafficDetails()
    {
        LiveTrafficGrid.Columns.Clear();
        LiveTrafficGrid.EnableRowVirtualization = LiveTrafficGrid.EnableColumnVirtualization = true;
        foreach (var (label, property, width) in new[]
        {
            ("Application", "Application", 150), ("PID", "ProcessId", 65), ("Protocol", "Protocol", 85),
            ("Local address", "LocalAddress", 135), ("Local port", "LocalPort", 80),
            ("Remote address", "RemoteAddress", 140), ("Remote port", "RemotePort", 85),
            ("Hostname", "Hostname", 160), ("Country", "Country", 100), ("Status", "Status", 240),
            ("Download", "DownloadBytes", 110), ("Upload", "UploadBytes", 110), ("Policy", "Policy", 100),
            ("Last seen", "LastSeenLocal", 150), ("Executable", "ExecutablePath", 300)
        })
        {
            var column = new DataGridTextColumn { Header = label, Binding = new Binding(property), Width = width };
            LocalizationService.SetSource(column, label); LiveTrafficGrid.Columns.Add(column);
        }
        if (LiveTrafficGrid.Parent is FrameworkElement border && border.Parent is Grid grid)
        {
            grid.RowDefinitions.Insert(1, new RowDefinition { Height = GridLength.Auto });
            foreach (UIElement child in grid.Children) if (Grid.GetRow(child) >= 1) Grid.SetRow(child, Grid.GetRow(child) + 1);
            var filters = new WrapPanel { Margin = new Thickness(5, 0, 5, 8) };
            foreach (var (label, input) in new (string, Control)[] { ("Application", _applicationFilter), ("Remote host", _hostFilter), ("Country", _countryFilter), ("Port", _portFilter), ("Policy", _policyFilter) })
            {
                var field = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
                field.Children.Add(WorkflowNavigation.Text(label, 11)); field.Children.Add(input); filters.Children.Add(field);
                if (input is TextBox text) text.TextChanged += (_, _) => LiveTrafficView?.Refresh();
            }
            _policyFilter.SelectionChanged += (_, _) => LiveTrafficView?.Refresh();
            Grid.SetRow(filters, 1); grid.Children.Add(filters);
        }
    }

    private bool FilterLiveTrafficDetails(LiveTrafficRow row)
    {
        if (!row.Application.Contains(_applicationFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        var host = _hostFilter.Text.Trim();
        if (!row.RemoteAddress.Contains(host, StringComparison.OrdinalIgnoreCase) && !row.Hostname.Contains(host, StringComparison.OrdinalIgnoreCase)) return false;
        if (!row.Country.Contains(_countryFilter.Text.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (_portFilter.Text.Length > 0 && (!int.TryParse(_portFilter.Text, out var port) || port is < 1 or > 65535 || port != row.LocalPort && port != row.RemotePort)) return false;
        return _policyFilter.SelectedIndex == 0 || row.Policy.Equals(_policyFilter.SelectedItem as string, StringComparison.OrdinalIgnoreCase);
    }

    private void ClearDetailedFlowFilters()
    {
        _applicationFilter.Clear(); _hostFilter.Clear(); _countryFilter.Clear(); _portFilter.Clear(); _policyFilter.SelectedIndex = 0;
    }

    private void InitializeWorkflowPreferences()
    {
        if (_workflow is null) return;
        var store = new WorkflowStateStore(registeredPages: _workflow.TopLevelPageNames);
        _workflow.Select(store.LoadPage());
        _workflow.NavigationChanged += (_, _) => store.SavePage(_workflow.CurrentPage);
    }
}
