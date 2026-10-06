using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Pulsatilla.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly DispatcherTimer _sampleTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _connectionsTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _packetUiTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly DispatcherTimer _systemTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _latencyTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _ownerRefreshTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer _usageSaveTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly Dictionary<string, CounterSnapshot> _previousCounters = new();
    private readonly Dictionary<string, string> _observedMacs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<string>> _dnsAnswers = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownApplicationPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _activeRdpConnections = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _packetSizeBuckets = new();
    private readonly BoundedPacketQueue _pendingPackets = new();
    private bool _reportedCaptureBacklog;
    private readonly ProcessConnectionResolver _processOwners = new();
    private readonly TrafficUsageStore _usageStore = new();
    private readonly WindowsFirewallRuleService _firewallRules = new();
    private readonly EventInsightService _eventInsight = new();
    private readonly Dictionary<string, LiveTrafficRow> _liveFlowByKey = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> _alertHistory = [];
    private NetworkInterface? _selectedAdapter;
    private bool _monitoring = true;
    private CancellationTokenSource? _scanCancellation;
    private long _packetCount;
    private int _ownerRefreshInProgress;
    private bool _latencyPending;
    private double _cpuLoadPercent;
    private double _memoryLoadPercent;
    private string? _lastSystemInfoFailure;
    private string? _lastCaptureError;
    private string _pingTarget = "1.1.1.1";
    private ulong? _previousIdle;
    private ulong? _previousKernel;
    private ulong? _previousUser;
    private static readonly HttpClient GeoIpClient = new() { Timeout = TimeSpan.FromSeconds(8) };

    public ObservableCollection<ScanResult> ScanResults { get; } = [];
    public ObservableCollection<ConnectionRow> Connections { get; } = [];
    public ObservableCollection<UsageSummary> UsageRows { get; } = [];
    public ObservableCollection<LiveTrafficRow> LiveTrafficRows { get; } = [];
    private ICollectionView LiveTrafficView { get; set; } = null!;

    public MainWindow()
    {
        LocalizationService.SetLanguage(LocalizationService.LoadSavedCode());
        InitializeComponent();
        LanguageSelector.ItemsSource = LocalizationService.Languages;
        LanguageSelector.SelectedValue = LocalizationService.CurrentCode;
        LocalizationService.Apply(this);
        DataContext = this;
        InitializeProtection();
        InitializePresentation();
        InitializeWorkflow();
        LiveTrafficView = CollectionViewSource.GetDefaultView(LiveTrafficRows);
        LiveTrafficView.Filter = FilterLiveTraffic;
        LiveTrafficGrid.ItemsSource = LiveTrafficView;
        _sampleTimer.Tick += (_, _) => SampleTraffic();
        _connectionsTimer.Tick += (_, _) => RefreshConnections();
        _packetUiTimer.Tick += (_, _) => DrainPacketQueue();
        _systemTimer.Tick += (_, _) => RefreshSystemInfo();
        _latencyTimer.Tick += (_, _) => _ = SamplePingAsync();
        _ownerRefreshTimer.Tick += async (_, _) => await RefreshProcessOwnersAsync();
        _usageSaveTimer.Tick += async (_, _) => await SaveUsageAndRefreshAsync();
        PingLatencyChart.MinimumScale = 250;
        SystemActivityChart.MinimumScale = 100;
        LoadAdapters();
        _sampleTimer.Start();
        _connectionsTimer.Start();
        _packetUiTimer.Start();
        _systemTimer.Start();
        _ownerRefreshTimer.Start();
        _usageSaveTimer.Start();
        RefreshConnections();
        RefreshSystemInfo();
        RefreshFirewallLog();
        RefreshUsageView();
        UpdateAdminStatus();
        RefreshCaptureDiagnostics();
        LogDirectoryText.Text = AppLogger.LogDirectory;
        RefreshErrorLogPreview();
        _ = RefreshHardwareDetailsAsync();
        _ = RefreshProcessOwnersAsync();
        _ = RefreshWifiInfoAsync();
    }

    private static string T(string source) => LocalizationService.Translate(source);
    private static string L(FormattableString text) => LocalizationService.Format(text);

    private void LanguageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageSelector.SelectedValue is not string code) return;
        if (!LocalizationService.SetLanguage(code)) return;
        LocalizationService.Apply(this);
        if (MonitorButton is null) return;
        MonitorButton.Content = T(_monitoring ? "Pause monitoring" : "Resume monitoring");
        MonitorStatus.Text = T(_monitoring ? "LIVE MONITORING" : "MONITORING PAUSED");
        if (UsageGrid is not null) UsageGrid_SelectionChanged(UsageGrid, e);
    }

    private void LoadAdapters()
    {
        LoadAdapterInventory();
        if (_networkPath is not null) _ = _networkPath.RefreshAsync();
    }

    private void LoadAdapterInventory()
    {
        var selectedId = _selectedAdapter?.Id;
        var adapters = EnumerateAdaptersSafely()
            .Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .OrderBy(adapter => adapter.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        AdapterList.Items.Clear();
        foreach (var adapter in adapters)
        {
            var properties = GetAdapterPropertiesSafely(adapter);
            if (properties is null) continue; // Hot removal can race enumeration.
            var address = properties.UnicastAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            var item = new ListBoxItem
            {
                Tag = adapter, Padding = new Thickness(10, 9, 6, 9), Margin = new Thickness(0, 2, 0, 2),
                Background = Brushes.Transparent, Foreground = ThemeService.Brush("D8E4DA"),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = adapter.Name, FontSize = 12, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = $"{adapter.OperationalStatus}  /  {address?.ToString() ?? "No IPv4"}", FontSize = 10,
                            Foreground = ThemeService.Brush("829488"), Margin = new Thickness(0, 4, 0, 0) }
                    }
                }
            };
            AdapterList.Items.Add(item);
            if (adapter.Id == selectedId) AdapterList.SelectedItem = item;
        }
        if (adapters.Length == 0) LastUpdateLabel.Text = T("No network adapters found");
    }

    private void AdapterList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterList.SelectedItem is not ListBoxItem { Tag: NetworkInterface adapter }) return;
        if (!_applyingRoutedAdapter && _selectedAdapter?.Id != adapter.Id)
        {
            if (_inspection.Snapshot.IsActive) _inspection.Stop("Manual interface choice changed; restart inspection explicitly.");
            _networkPath?.SelectManualAdapter(adapter.Id);
        }
        var properties = GetAdapterPropertiesSafely(adapter);
        if (properties is null)
        {
            _inspection.Stop("Selected adapter disappeared; refresh Network Path.");
            _selectedAdapter = null; Ipv4Value.Text = GatewayValue.Text = "Unknown";
            SelectedAdapterLabel.Text = "Selected adapter unavailable";
            return;
        }
        _selectedAdapter = adapter;
        Ipv4Value.Text = properties.UnicastAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "-";
        GatewayValue.Text = properties.GatewayAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "-";
        MacValue.Text = FormatMac(adapter.GetPhysicalAddress().GetAddressBytes());
        LinkSpeedValue.Text = adapter.Speed > 0 ? $"{adapter.Speed / 1_000_000d:0.#} Mbps" : "Unknown";
        AdapterStatus.Text = adapter.OperationalStatus.ToString();
        SelectedAdapterLabel.Text = adapter.Name;
        var prefix = properties.UnicastAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork)?.PrefixLength;
        if (prefix is not null) NetworkRangeInput.Text = $"{Ipv4Value.Text}/{prefix.Value}";
        if (TargetInput.Text.Length == 0 && Ipv4Value.Text != "-") TargetInput.Text = Ipv4Value.Text;
        if (GatewayValue.Text != "-") { _pingTarget = GatewayValue.Text; PingTargetInput.Text = _pingTarget; }
        _previousCounters.Remove(adapter.Id);
        ThroughputChart.Clear();
        ThroughputFreezeButton.Content = "Freeze view";
        RefreshCaptureDiagnostics();
    }

    private void SampleTraffic()
    {
        IEnumerable<NetworkInterface> adapters = _selectedAdapter is null
            ? EnumerateAdaptersSafely().Where(adapter => adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            : [_selectedAdapter];
        long received = 0, sent = 0;
        foreach (var adapter in adapters)
        {
            try
            {
                var stats = adapter.GetIPv4Statistics();
                var current = new CounterSnapshot(stats.BytesReceived, stats.BytesSent, DateTime.UtcNow);
                if (_previousCounters.TryGetValue(adapter.Id, out var previous))
                {
                    var elapsed = Math.Max(0.001, (current.Timestamp - previous.Timestamp).TotalSeconds);
                    received += (long)Math.Max(0, (current.Received - previous.Received) / elapsed);
                    sent += (long)Math.Max(0, (current.Sent - previous.Sent) / elapsed);
                }
                _previousCounters[adapter.Id] = current;
            }
            catch (NetworkInformationException) { _previousCounters.Remove(adapter.Id); }
        }
        ThroughputChart.AddSample(received, sent);
        ThroughputSummaryText.Text = ThroughputChart.Summary;
        MatrixBackground.SetTrafficRate(received + sent);
        DownloadRate.Text = FormatRate(received);
        UploadRate.Text = FormatRate(sent);
        LastUpdateLabel.Text = L($"Updated {DateTime.Now:HH:mm:ss}  /  500 ms sampling");
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadAdapters();
        _ = RefreshWifiInfoAsync();
        LastUpdateLabel.Text = L($"Adapters refreshed {DateTime.Now:HH:mm:ss}");
    }

    private async Task RefreshWifiInfoAsync()
    {
        if (_workflowProcessLifetime.IsCancellationRequested || Interlocked.Exchange(ref _wifiInformationPending, 1) != 0) return;
        try
            {
                var result = await SafeProcessRunner.RunAsync(SafeProcessRunner.SystemExecutable("netsh.exe"),
                    ["wlan", "show", "interfaces"], TimeSpan.FromSeconds(3), _workflowProcessLifetime.Token, 65536);
                if (_workflowProcessLifetime.IsCancellationRequested) return;
                if (result.ExitCode != 0 || result.OutputTruncated) throw new IOException("Wi-Fi details were unavailable or incomplete.");
                var details = result.Output.Split(Environment.NewLine)
                    .Select(line => line.Trim())
                    .Where(line => line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("BSSID", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Signal", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Channel", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                WifiInfoValue.Text = details.Length == 0 ? "Not connected / unavailable" : string.Join("  |  ", details);
            }
        catch (Exception) { WifiInfoValue.Text = T("Wi-Fi details unavailable"); }
        finally { Interlocked.Exchange(ref _wifiInformationPending, 0); }
    }

    private void MonitorButton_Click(object sender, RoutedEventArgs e)
    {
        _monitoring = !_monitoring;
        if (_monitoring)
        {
            _sampleTimer.Start(); MonitorButton.Content = T("Pause monitoring");
            MonitorStatus.Text = T("LIVE MONITORING"); MonitorIndicator.Fill = ThemeService.Brush("75DE88");
        }
        else
        {
            _sampleTimer.Stop(); MonitorButton.Content = T("Resume monitoring");
            MonitorStatus.Text = T("MONITORING PAUSED"); MonitorIndicator.Fill = ThemeService.Brush("DDB15B");
            MatrixBackground.SetTrafficRate(0);
        }
    }

    private async void PingSweepButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNetwork(NetworkRangeInput.Text, out var addresses, out var error))
        {
            ScanStatusLabel.Text = error;
            return;
        }
        ScanResults.Clear();
        _scanCancellation?.Cancel();
        _scanCancellation = new CancellationTokenSource();
        var token = _scanCancellation.Token;
        PingSweepButton.IsEnabled = false;
        CancelScanButton.IsEnabled = true;
        ScanStatusLabel.Text = L($"Scanning {addresses.Count:N0} addresses...");
        var completed = 0;
        using var throttle = new SemaphoreSlim(64);
        try
        {
            await Task.WhenAll(addresses.Select(async address =>
            {
                await throttle.WaitAsync(token);
                try
                {
                    using var ping = new Ping();
                    var reply = await ping.SendPingAsync(address, 700).WaitAsync(TimeSpan.FromSeconds(2), token);
                    if (reply.Status == IPStatus.Success)
                    {
                        var dns = await ResolveDnsAsync(address);
                        await Dispatcher.InvokeAsync(() => ScanResults.Add(new ScanResult(address.ToString(), "Alive", "-", dns)));
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception) { }
                finally
                {
                    throttle.Release();
                    var count = Interlocked.Increment(ref completed);
                    if (count % 16 == 0) await Dispatcher.InvokeAsync(() => ScanStatusLabel.Text = L($"Scanned {count:N0} / {addresses.Count:N0} addresses"));
                }
            }));
            ScanStatusLabel.Text = L($"Complete. {ScanResults.Count:N0} host(s) responded.");
        }
        catch (OperationCanceledException) { ScanStatusLabel.Text = T("Scan cancelled."); }
        finally { PingSweepButton.IsEnabled = true; CancelScanButton.IsEnabled = false; }
    }

    private void CancelScanButton_Click(object sender, RoutedEventArgs e) => _scanCancellation?.Cancel();

    private async void FastScanButton_Click(object sender, RoutedEventArgs e) =>
        await ScanTargetPortsAsync([22, 53, 80, 443, 445, 3389, 8080]);

    private async void FullScanButton_Click(object sender, RoutedEventArgs e) =>
        await ScanTargetPortsAsync(Enumerable.Range(1, 1024));

    private async Task ScanTargetPortsAsync(IEnumerable<int> ports)
    {
        if (!IPAddress.TryParse(TargetInput.Text.Trim(), out var target) || target.AddressFamily != AddressFamily.InterNetwork)
        {
            ScanStatusLabel.Text = T("Enter a valid IPv4 target.");
            return;
        }
        var portList = ports.ToArray();
        ScanResults.Clear();
        PingSweepButton.IsEnabled = false;
        ScanStatusLabel.Text = L($"Checking {portList.Length:N0} TCP ports on {target}...");
        using var throttle = new SemaphoreSlim(96);
        var openPorts = new ConcurrentBag<int>();
        try
        {
            await Task.WhenAll(portList.Select(async port =>
            {
                await throttle.WaitAsync();
                try
                {
                    using var client = new TcpClient();
                    await client.ConnectAsync(target, port).WaitAsync(TimeSpan.FromMilliseconds(450));
                    openPorts.Add(port);
                }
                catch (Exception) { }
                finally { throttle.Release(); }
            }));
            var ordered = openPorts.Order().ToArray();
            ScanResults.Add(new ScanResult(target.ToString(), "Scanned", string.Join(", ", ordered), await ResolveDnsAsync(target)));
            ScanStatusLabel.Text = L($"Complete. {ordered.Length} open port(s) found.");
        }
        finally { PingSweepButton.IsEnabled = true; }
    }

    private async void NmapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workflowProcessLifetime.IsCancellationRequested || Interlocked.Exchange(ref _nmapOperationPending, 1) != 0) return;
        var button = sender as Button;
        if (button is not null) button.IsEnabled = false;
        try
        {
            var target = ActiveServiceScanner.ValidateTarget(TargetInput.Text.Trim(), 80);
            var executable = SelectNmapExecutable();
            if (executable is null) { ScanStatusLabel.Text = "Nmap is NOT INSTALLED or no executable was selected. No scan started."; return; }
            if (MessageBox.Show(this, $"ACTIVE NETWORK OPERATION\n\nRun Nmap service and OS detection against {target}? Inspect only a target you are authorized to scan.\n\nExecutable: {executable}\nArguments: -sV -O {target}\nDeadline: two minutes.",
                "Explicit Nmap scan", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            ScanStatusLabel.Text = T("Running Nmap..."); NmapOutput.Clear();
            var result = await SafeProcessRunner.RunAsync(executable, ["-sV", "-O", target.ToString()],
                TimeSpan.FromMinutes(2), _workflowProcessLifetime.Token, 262144);
            if (_workflowProcessLifetime.IsCancellationRequested) return;
            NmapOutput.Text = string.IsNullOrWhiteSpace(result.Output) ? result.Error : result.Output +
                (string.IsNullOrWhiteSpace(result.Error) ? "" : "\n\n" + result.Error);
            if (result.OutputTruncated) NmapOutput.Text += "\n[Output preview limited to 256 KiB per stream.]";
            ScanStatusLabel.Text = result.ExitCode == 0 ? T("Nmap scan complete.") : L($"Nmap exited with code {result.ExitCode}.");
        }
        catch (OperationCanceledException) { if (!_workflowProcessLifetime.IsCancellationRequested) ScanStatusLabel.Text = "Nmap operation cancelled; its owned process was stopped."; }
        catch (Exception ex) when (ex is IOException or ArgumentException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            AppLogger.WriteEvent("WARN", "Nmap operation unavailable, rejected or exceeded its deadline; no process output was logged.");
            NmapOutput.Text = ex is ArgumentException ? "Enter a literal target IP and select a regular local nmap.exe executable." : "Nmap failed or exceeded the two-minute deadline. Any running process started for this operation was stopped.";
            ScanStatusLabel.Text = T("Nmap unavailable or failed.");
        }
        finally { Interlocked.Exchange(ref _nmapOperationPending, 0); if (button is not null) button.IsEnabled = true; }
    }

    private void RefreshConnections()
    {
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnections = properties.GetActiveTcpConnections();
            var currentRdp = tcpConnections
                .Where(connection => connection.State == TcpState.Established && connection.LocalEndPoint.Port == 3389)
                .Select(connection => connection.RemoteEndPoint.ToString())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var endpoint in currentRdp.Except(_activeRdpConnections))
                AddEvent("RDP", $"New established RDP connection from {endpoint}");
            _activeRdpConnections.Clear();
            _activeRdpConnections.UnionWith(currentRdp);

            var rows = tcpConnections
                .Select(connection => new ConnectionRow(connection.LocalEndPoint.ToString(), connection.RemoteEndPoint.ToString(), "TCP", connection.State.ToString()))
                .Concat(properties.GetActiveTcpListeners().Select(endpoint => new ConnectionRow(endpoint.ToString(), "-", "TCP", "LISTEN")))
                .Concat(properties.GetActiveUdpListeners().Select(endpoint => new ConnectionRow(endpoint.ToString(), "-", "UDP", "LISTEN")))
                .OrderBy(row => row.State == "LISTEN" ? 0 : 1)
                .ThenBy(row => row.LocalAddress, StringComparer.OrdinalIgnoreCase)
                .Take(500)
                .ToArray();
            Connections.Clear();
            foreach (var row in rows) Connections.Add(row);
            DashboardConnectionsValue.Text = L($"{Connections.Count:N0} live endpoints");
        }
        catch (NetworkInformationException ex) { AddEvent("WARN", $"Connection inventory failed: {ex.Message}"); }
    }

    private async void SecurityScanButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetNetwork(NetworkRangeInput.Text, out var addresses, out var error))
        {
            SecurityStatusLabel.Text = error;
            return;
        }
        SecurityScanButton.IsEnabled = false;
        SecurityStatusLabel.Text = L($"Checking {addresses.Count:N0} local addresses...");
        var checkedCount = 0;
        var activeThisSweep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var throttle = new SemaphoreSlim(48);
        try
        {
            await Task.WhenAll(addresses.Select(async address =>
            {
                await throttle.WaitAsync();
                try
                {
                    var mac = await Task.Run(() => ResolveMac(address));
                    if (mac.Length > 0)
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            var key = address.ToString();
                            activeThisSweep.Add(key);
                            if (_observedMacs.TryGetValue(key, out var previous) && !previous.Equals(mac, StringComparison.OrdinalIgnoreCase))
                                AddEvent("ALERT", $"MAC changed for {address}: {previous} -> {mac}");
                            else if (!_observedMacs.ContainsKey(key))
                                AddEvent("HOST", $"{address} is active ({mac})");
                            _observedMacs[key] = mac;
                        });
                    }
                }
                catch (Exception) { }
                finally { throttle.Release(); Interlocked.Increment(ref checkedCount); }
            }));
            foreach (var address in _observedMacs.Keys.Where(key => !activeThisSweep.Contains(key)).ToArray())
            {
                AddEvent("DEVICE LEFT", $"{address} was not seen in the latest subnet sweep");
                _observedMacs.Remove(address);
            }
            SecurityStatusLabel.Text = L($"Subnet sweep complete. {checkedCount:N0} addresses checked.");
        }
        finally { SecurityScanButton.IsEnabled = true; }
    }

    private async void GeoIpButton_Click(object sender, RoutedEventArgs e)
    {
        if (!IPAddress.TryParse(TargetInput.Text.Trim(), out var address) || address.AddressFamily != AddressFamily.InterNetwork)
        {
            GeoIpOutput.Text = T("Enter a valid IPv4 address.");
            return;
        }
        var bytes = address.GetAddressBytes();
        if (bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 192 && bytes[1] == 168) || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31))
        {
            GeoIpOutput.Text = T("Private/local addresses do not have public GeoIP data.");
            return;
        }
        GeoIpOutput.Text = T("Looking up public GeoIP data...");
        try
        {
            using var response = await GeoIpClient.GetAsync($"https://ipapi.co/{address}/json/");
            response.EnsureSuccessStatusCode();
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = document.RootElement;
            string Read(string name) => root.TryGetProperty(name, out var value) ? value.ToString() : "Unknown";
            GeoIpOutput.Text = $"{Read("city")}, {Read("region")}, {Read("country_name")}\n{Read("org")}";
        }
        catch (Exception ex)
        {
            AppLogger.WriteException("WARN", "GeoIP lookup failed", ex);
            GeoIpOutput.Text = L($"GeoIP lookup failed: {ex.Message}");
        }
    }

    private void SetPingTargetButton_Click(object sender, RoutedEventArgs e)
    {
        var value = PingTargetInput.Text.Trim();
        if (Uri.CheckHostName(value) == UriHostNameType.Unknown)
        {
            PingStatusLabel.Text = T("Enter a valid host or IP address.");
            return;
        }
        _pingTarget = value;
        _latencyTimer.Start();
        PingStatusLabel.Text = L($"Monitoring {_pingTarget}");
    }

    private async Task SamplePingAsync()
    {
        if (_latencyPending || string.IsNullOrWhiteSpace(_pingTarget)) return;
        _latencyPending = true;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(_pingTarget, 800).WaitAsync(TimeSpan.FromSeconds(1));
            if (reply.Status == IPStatus.Success)
            {
                PingLatencyChart.AddSample(reply.RoundtripTime, 0);
                PingStatusLabel.Text = L($"{_pingTarget}  /  {reply.RoundtripTime} ms  /  TTL {reply.Options?.Ttl ?? 0}");
            }
            else
            {
                PingLatencyChart.AddSample(0, 0);
                PingStatusLabel.Text = $"{_pingTarget}  /  {reply.Status}";
            }
        }
        catch (Exception ex)
        {
            PingLatencyChart.AddSample(0, 0);
            PingStatusLabel.Text = L($"{_pingTarget}  /  no response ({ex.GetType().Name})");
        }
        finally { _latencyPending = false; }
    }

    private void RefreshHardwareButton_Click(object sender, RoutedEventArgs e) => _ = RefreshHardwareDetailsAsync();

    private async Task RefreshHardwareDetailsAsync()
    {
        if (_workflowProcessLifetime.IsCancellationRequested || Interlocked.Exchange(ref _hardwareInformationPending, 1) != 0) return;
        HardwareRefreshStatus.Text = T("Reading Windows hardware inventory...");
        try
        {
            const string query = "$OutputEncoding=[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); " +
                "$processors=@(Get-CimInstance Win32_Processor | Select-Object Name,Manufacturer,SocketDesignation,NumberOfCores,NumberOfLogicalProcessors,MaxClockSpeed); " +
                "$board=Get-CimInstance Win32_BaseBoard | Select-Object Manufacturer,Product,Version; " +
                "$bios=Get-CimInstance Win32_BIOS | Select-Object Manufacturer,SMBIOSBIOSVersion,ReleaseDate; " +
                "$gpus=@(Get-CimInstance Win32_VideoController | Select-Object Name,VideoProcessor,DriverVersion,CurrentHorizontalResolution,CurrentVerticalResolution,CurrentRefreshRate); " +
                "$memory=@(Get-CimInstance Win32_PhysicalMemory | Select-Object DeviceLocator,Manufacturer,PartNumber,Speed,@{Name='CapacityGB';Expression={[math]::Round($_.Capacity/1GB,1)}}); " +
                "$disks=@(Get-CimInstance Win32_DiskDrive | Select-Object Model,InterfaceType,MediaType,Status,@{Name='CapacityGB';Expression={[math]::Round($_.Size/1GB,0)}}); " +
                "$adapters=@(Get-CimInstance Win32_NetworkAdapter | Where-Object {$_.PhysicalAdapter} | Select-Object Name,Manufacturer,NetEnabled,MACAddress,@{Name='SpeedMbps';Expression={[math]::Round($_.Speed/1MB,0)}}); " +
                "[pscustomobject]@{processors=$processors;baseboard=$board;bios=$bios;gpus=$gpus;memoryModules=$memory;physicalDisks=$disks;networkAdapters=$adapters} | ConvertTo-Json -Depth 5 -Compress";
            var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");
            var result = await SafeProcessRunner.RunAsync(executable, ["-NoProfile", "-NonInteractive", "-Command", query],
                TimeSpan.FromSeconds(15), _workflowProcessLifetime.Token, 262144);
            if (_workflowProcessLifetime.IsCancellationRequested) return;
            if (result.ExitCode != 0 || result.OutputTruncated) throw new IOException("Windows hardware inventory was unavailable or incomplete.");
            var output = result.Output;

            using var document = JsonDocument.Parse(output);
            var root = document.RootElement;
            var hardware = new StringBuilder();
            AppendHardwareItems(hardware, root, "processors", "CPU / SoC", item =>
                $"{ReadProperty(item, "Name")} | {ReadProperty(item, "Manufacturer")} | {ReadProperty(item, "SocketDesignation")} | " +
                $"{ReadProperty(item, "NumberOfCores")} cores / {ReadProperty(item, "NumberOfLogicalProcessors")} threads | max {ReadProperty(item, "MaxClockSpeed")} MHz");
            AppendHardwareItems(hardware, root, "baseboard", "BASEBOARD", item =>
                $"{ReadProperty(item, "Manufacturer")} {ReadProperty(item, "Product")} | version {ReadProperty(item, "Version")}");
            AppendHardwareItems(hardware, root, "bios", "BIOS / UEFI", item =>
                $"{ReadProperty(item, "Manufacturer")} | {ReadProperty(item, "SMBIOSBIOSVersion")} | {ReadProperty(item, "ReleaseDate")}");
            AppendHardwareItems(hardware, root, "gpus", "GPU", item =>
                $"{ReadProperty(item, "Name")} | {ReadProperty(item, "VideoProcessor")} | driver {ReadProperty(item, "DriverVersion")} | " +
                $"{ReadProperty(item, "CurrentHorizontalResolution")}x{ReadProperty(item, "CurrentVerticalResolution")} @ {ReadProperty(item, "CurrentRefreshRate")} Hz");
            AppendHardwareItems(hardware, root, "memoryModules", "MEMORY MODULE", item =>
                $"{ReadProperty(item, "DeviceLocator")} | {ReadProperty(item, "CapacityGB")} GB | {ReadProperty(item, "Speed")} MT/s | {ReadProperty(item, "PartNumber")} | {ReadProperty(item, "Manufacturer")}");
            AppendHardwareItems(hardware, root, "physicalDisks", "PHYSICAL DISK", item =>
                $"{ReadProperty(item, "Model")} | {ReadProperty(item, "CapacityGB")} GB | {ReadProperty(item, "InterfaceType")} | {ReadProperty(item, "MediaType")} | {ReadProperty(item, "Status")}");
            AppendHardwareItems(hardware, root, "networkAdapters", "NETWORK ADAPTER", item =>
                $"{ReadProperty(item, "Name")} | {ReadProperty(item, "Manufacturer")} | {ReadProperty(item, "SpeedMbps")} Mbps | {ReadProperty(item, "MACAddress")} | enabled: {ReadProperty(item, "NetEnabled")}");
            HardwareDetailsText.Text = hardware.Length > 0 ? hardware.ToString().TrimEnd() : "No hardware inventory was returned.";
            HardwareRefreshStatus.Text = L($"Windows CIM inventory refreshed {DateTime.Now:HH:mm:ss}");
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            HardwareDetailsText.Text = "Hardware inventory failed or exceeded its deadline. The owned inventory process was stopped.";
            HardwareRefreshStatus.Text = T("Hardware inventory unavailable");
            AppLogger.WriteEvent("WARN", "Hardware inventory refresh unavailable, incomplete or exceeded its deadline.");
            RefreshErrorLogPreview();
        }
        finally { Interlocked.Exchange(ref _hardwareInformationPending, 0); }
    }

    private static void AppendHardwareItems(StringBuilder output, JsonElement root, string name, string title,
        Func<JsonElement, string> formatter)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return;
        var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().ToArray() : [value];
        foreach (var item in items)
        {
            output.AppendLine(title);
            output.AppendLine($"  {formatter(item)}");
        }
    }

    private static string ReadProperty(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ToString()
            : "N/A";

    private static bool HasAdministratorRights()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch (Exception) { return false; }
    }

    private void UpdateAdminStatus()
    {
        var isAdministrator = HasAdministratorRights();
        AdminStatusText.Text = T(isAdministrator
            ? "Administrator rights: YES. Raw capture and firewall operations can request privileged access."
            : "Administrator rights: NO. Monitoring works, but raw capture and app firewall changes may be denied.");
        AdminStatusText.Foreground = new SolidColorBrush(isAdministrator
            ? ThemeService.Color("83E89A")
            : ThemeService.Color("E5C16A"));
        RestartElevatedButton.Visibility = isAdministrator ? Visibility.Collapsed : Visibility.Visible;
        DashboardRestartAdminButton.Visibility = isAdministrator ? Visibility.Collapsed : Visibility.Visible;
        RefreshCaptureDiagnostics();
    }

    private void RestartElevatedButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var executable = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("The app executable path is unknown.");
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            AdminStatusText.Text = L($"Administrator restart was cancelled or failed: {ex.Message}");
            AppLogger.WriteEvent("INFO", $"Administrator restart was cancelled or failed: {ex.Message}");
        }
    }

    private void RefreshCaptureDiagnosticsButton_Click(object sender, RoutedEventArgs e) => RefreshCaptureDiagnostics();

    private void RefreshErrorLogButton_Click(object sender, RoutedEventArgs e) => RefreshErrorLogPreview();

    private void OpenLogsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppLogger.LogDirectory);
            Process.Start(new ProcessStartInfo(AppLogger.LogDirectory) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppLogger.WriteException("ERROR", "Could not open the log folder", ex);
            MessageBox.Show(this, $"Could not open the log folder.\n{AppLogger.LogDirectory}\n\n{ex.Message}",
                "Log folder unavailable", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void RefreshErrorLogPreview() => ErrorLogPreview.Text = AppLogger.ReadRecentLines();

    private void RefreshCaptureDiagnostics()
    {
        var adapterAddress = GetSelectedIpv4();
        var status = adapterAddress is null
            ? "No selected adapter with IPv4. Select an adapter on Dashboard first."
            : HasAdministratorRights()
                ? $"Ready to test raw IPv4 capture on {adapterAddress}. Starting capture will verify socket access."
                : $"Selected adapter: {adapterAddress}. Administrator rights are missing; raw socket access may fail with 10013.";
        if (!string.IsNullOrWhiteSpace(_lastCaptureError)) status += $"\nLast capture error: {_lastCaptureError}";
        CaptureReadinessText.Text = status;
    }

    private void RefreshSystemInfo()
    {
        try
        {
            var cpuText = "Unavailable";
            if (GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
            {
                var idle = ToUInt64(idleTime);
                var kernel = ToUInt64(kernelTime);
                var user = ToUInt64(userTime);
                if (_previousIdle.HasValue && _previousKernel.HasValue && _previousUser.HasValue)
                {
                    var total = (kernel - _previousKernel.Value) + (user - _previousUser.Value);
                    var idleDelta = idle - _previousIdle.Value;
                    var usage = total == 0 ? 0 : Math.Clamp(100d * (total - idleDelta) / total, 0, 100);
                    _cpuLoadPercent = usage;
                    cpuText = $"{usage:0.0}%";
                }
                _previousIdle = idle; _previousKernel = kernel; _previousUser = user;
            }
            var memory = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            var hasMemoryStatus = GlobalMemoryStatusEx(ref memory);
            var memoryText = hasMemoryStatus
                ? $"{memory.MemoryLoad}% used  /  {memory.TotalPhysical / 1024d / 1024 / 1024:0.0} GB total"
                : "Unavailable";
            if (hasMemoryStatus) _memoryLoadPercent = memory.MemoryLoad;
            var drives = DriveInfo.GetDrives().Where(drive => drive.IsReady).Take(5)
                .Select(drive => $"{drive.Name.TrimEnd('\\')}  {drive.AvailableFreeSpace / 1024d / 1024 / 1024:0.0} GB free")
                .ToArray();
            var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
            SystemInfoText.Text = L($"CPU load:   {cpuText}\nRAM:        {memoryText}\nProcessors: {Environment.ProcessorCount}\nArchitecture:{RuntimeInformation.ProcessArchitecture}\nOS:         {Environment.OSVersion.VersionString}\nUptime:     {uptime.Days}d {uptime.Hours}h {uptime.Minutes}m\n\nLogical volumes\n{string.Join(Environment.NewLine, drives)}");
            _lastSystemInfoFailure = null;
            DashboardCpuValue.Text = $"{_cpuLoadPercent:0}%";
            DashboardMemoryValue.Text = $"{_memoryLoadPercent:0}%";
            SystemActivityChart.AddSample(_cpuLoadPercent, _memoryLoadPercent);
        }
        catch (Exception ex)
        {
            SystemInfoText.Text = L($"System information unavailable: {ex.Message}");
            var failureKey = $"{ex.GetType().FullName}:{ex.Message}";
            if (_lastSystemInfoFailure != failureKey)
            {
                AppLogger.WriteException("ERROR", "System telemetry refresh failed", ex);
                _lastSystemInfoFailure = failureKey;
            }
        }
    }

    private void RefreshFirewallButton_Click(object sender, RoutedEventArgs e) => RefreshFirewallLog();

    private void RefreshFirewallLog()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "LogFiles", "Firewall", "pfirewall.log");
        if (!File.Exists(path))
        {
            FirewallLogBox.Text = T("Firewall log is not enabled or is not present. This app does not change Windows Firewall settings.");
            FirewallStatusLabel.Text = T("Log unavailable; no system settings changed.");
            return;
        }
        try
        {
            var lines = File.ReadLines(path).Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')).TakeLast(50).ToArray();
            FirewallLogBox.Text = lines.Length == 0 ? "No firewall events recorded." : string.Join(Environment.NewLine, lines);
            FirewallStatusLabel.Text = L($"Showing latest {lines.Length} entries; read-only.");
        }
        catch (Exception ex)
        {
            AppLogger.WriteException("WARN", "Windows Firewall log read failed", ex);
            FirewallLogBox.Text = L($"Cannot read firewall log: {ex.Message}");
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export network monitor data",
            Filter = "JSON report|*.json|CSV table|*.csv|Text report|*.txt",
            FileName = $"pulsatilla-report-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var report = new
            {
                exportedAtUtc = DateTime.UtcNow,
                adapters = EnumerateAdaptersSafely().Select(adapter => new
                {
                    adapter.Name, Status = adapter.OperationalStatus.ToString(), adapter.Description,
                    Mac = FormatMac(adapter.GetPhysicalAddress().GetAddressBytes()),
                    IPv4 = GetAdapterPropertiesSafely(adapter)?.UnicastAddresses.FirstOrDefault(item => item.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString()
                }),
                scanResults = ScanResults,
                connections = Connections,
                securityEvents = EventsList.Items.Cast<object>()
                    .Select(item => item is ListBoxItem { Content: TextBlock eventText } ? eventText.Text : item.ToString() ?? "")
                    .ToArray(),
                packetCount = Interlocked.Read(ref _packetCount),
                packetSizes = _packetSizeBuckets,
                packetSources = _sourceTracker.SourcePacketCounts,
                topSoftwareSources = _sourceTracker.TopApplications(),
                dnsAnswers = _dnsAnswers,
                system = SystemInfoText.Text
            };
            var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            var output = extension == ".csv" ? ToCsv(report) : JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dialog.FileName, output);
            LastUpdateLabel.Text = L($"Exported {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception ex)
        {
            AppLogger.WriteException("ERROR", "Report export failed", ex);
            RefreshErrorLogPreview();
            MessageBox.Show(this, ex.Message, "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string ToCsv(object report)
    {
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(report));
        var rows = new List<string> { "section,value" };
        foreach (var property in json.RootElement.EnumerateObject())
            rows.Add($"{CsvEscape(property.Name)},{CsvEscape(property.Value.ToString())}");
        return string.Join(Environment.NewLine, rows);
    }

    private static string CsvEscape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeFileTime { public uint Low; public uint High; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
        public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(out NativeFileTime idle, out NativeFileTime kernel, out NativeFileTime user);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static ulong ToUInt64(NativeFileTime value) => ((ulong)value.High << 32) | value.Low;

    private void RefreshUsageButton_Click(object sender, RoutedEventArgs e) => RefreshUsageView();

    private void UsageGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsageGrid.SelectedItem is not UsageSummary summary || string.IsNullOrWhiteSpace(summary.ExecutablePath))
        {
            BlockApplicationButton.IsEnabled = false;
            return;
        }
        var blocked = _firewallRules.IsBlocked(summary.ExecutablePath);
        BlockApplicationButton.Content = T(blocked ? "Unblock selected app" : "Block selected app");
        BlockApplicationButton.IsEnabled = true;
    }

    private async void BlockApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        if (UsageGrid.SelectedItem is not UsageSummary summary || string.IsNullOrWhiteSpace(summary.ExecutablePath)) return;
        var blocked = _firewallRules.IsBlocked(summary.ExecutablePath);
        var action = blocked ? "remove Pulsatilla's inbound and outbound firewall rules for" : "block inbound and outbound network traffic for";
        if (MessageBox.Show(this, $"Are you sure you want to {action}\n\n{summary.Application}\n{summary.ExecutablePath}?\n\nThis changes Windows Firewall rules and may interrupt the app.",
            "Confirm firewall change", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        BlockApplicationButton.IsEnabled = false;
        var result = await _firewallRules.SetBlockedAsync(summary.ExecutablePath, !blocked);
        AddEvent(result.Success ? "FIREWALL" : "FIREWALL ERROR", $"{summary.Application}: {result.Message}");
        if (result.Success)
        {
            try { RecordApplicationPolicy(summary.ExecutablePath, blocked ? ApplicationTrust.Monitor : ApplicationTrust.Blocked); }
            catch (Exception ex) { AddEvent("WARN", "Firewall changed, but the application policy could not be saved: " + ex.Message); }
        }
        MessageBox.Show(this, result.Message, result.Success ? "Firewall updated" : "Firewall update failed",
            MessageBoxButton.OK, result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        var selectedRow = UsageGrid.SelectedItem as UsageSummary;
        var isNowBlocked = selectedRow is not null && _firewallRules.IsBlocked(selectedRow.ExecutablePath);
        BlockApplicationButton.Content = T(isNowBlocked ? "Unblock selected app" : "Block selected app");
        BlockApplicationButton.IsEnabled = selectedRow is not null && !string.IsNullOrWhiteSpace(selectedRow.ExecutablePath);
    }

    private void RefreshUsageView()
    {
        var period = UsagePeriodBox.SelectedIndex switch
        {
            0 => TimeSpan.FromHours(1),
            1 => TimeSpan.FromHours(6),
            3 => TimeSpan.FromDays(7),
            _ => TimeSpan.FromHours(24)
        };
        UsageRows.Clear();
        foreach (var row in _usageStore.Query(period)) UsageRows.Add(row);
    }

    private async Task RefreshProcessOwnersAsync()
    {
        if (_workflowProcessLifetime.IsCancellationRequested || Interlocked.Exchange(ref _ownerRefreshInProgress, 1) != 0) return;
        try
        {
            await Task.Run(_processOwners.Refresh);
            if (_workflowProcessLifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            await Dispatcher.InvokeAsync(() => AttributionStatus.Text =
                $"Windows socket table: {_processOwners.TcpEndpointCount:N0} TCP / {_processOwners.UdpEndpointCount:N0} UDP endpoint entries"
                + (_processOwners.SnapshotTruncated ? " — bounded snapshot incomplete; packet ownership stays Unknown." : ""),
                DispatcherPriority.Background, _workflowProcessLifetime.Token);
        }
        catch (OperationCanceledException) when (_workflowProcessLifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted) { }
        catch (Exception ex)
        {
            if (_workflowProcessLifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted) return;
            AppLogger.WriteException("WARN", "Process attribution refresh failed", ex);
            try { await Dispatcher.InvokeAsync(() => AddEvent("WARN", "Process attribution unavailable; refresh Network Path before retrying."),
                DispatcherPriority.Background, _workflowProcessLifetime.Token); }
            catch (OperationCanceledException) when (_workflowProcessLifetime.IsCancellationRequested || Dispatcher.HasShutdownStarted) { }
        }
        finally { Interlocked.Exchange(ref _ownerRefreshInProgress, 0); }
    }

    private async Task SaveUsageAndRefreshAsync()
    {
        try { await _usageStore.SaveAsync(); }
        catch (Exception ex) { AddEvent("WARN", $"Could not save local usage history: {ex.Message}"); }
        RefreshUsageView();
    }

    private async void WifiSecurityButton_Click(object sender, RoutedEventArgs e)
    {
        if (_workflowProcessLifetime.IsCancellationRequested || Interlocked.Exchange(ref _wifiReviewPending, 1) != 0) return;
        WifiSecurityStatus.Text = T("Checking nearby Wi-Fi networks...");
        try
        {
            var result = await SafeProcessRunner.RunAsync(SafeProcessRunner.SystemExecutable("netsh.exe"),
                ["wlan", "show", "networks", "mode=bssid"], TimeSpan.FromSeconds(8), _workflowProcessLifetime.Token, 131072);
            if (_workflowProcessLifetime.IsCancellationRequested) return;
            if (result.ExitCode != 0 || result.OutputTruncated) throw new IOException("Wi-Fi review data was unavailable or incomplete.");
            var output = result.Output;
            var ssidMatch = System.Text.RegularExpressions.Regex.Match(WifiInfoValue.Text,
                @"SSID\s*:\s*(.*?)\s*(?:\||$)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            var currentSsid = ssidMatch.Success ? ssidMatch.Groups[1].Value.Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(currentSsid) || currentSsid.Contains("unavailable", StringComparison.OrdinalIgnoreCase))
            {
                WifiSecurityStatus.Text = T("Connect to Wi-Fi to check for same-name access points.");
                return;
            }

            var lines = output.Split(Environment.NewLine);
            var matchingNetwork = false;
            var bssids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var hasOpenAuthentication = false;
            foreach (var line in lines)
            {
                var ssid = System.Text.RegularExpressions.Regex.Match(line, @"SSID\s+\d+\s*:\s*(.*)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (ssid.Success) matchingNetwork = ssid.Groups[1].Value.Trim().Equals(currentSsid, StringComparison.OrdinalIgnoreCase);
                if (!matchingNetwork) continue;
                var bssid = System.Text.RegularExpressions.Regex.Match(line, @"BSSID\s+\d+\s*:\s*([0-9a-f:-]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (bssid.Success) bssids.Add(bssid.Groups[1].Value.Trim());
                if (line.Contains("Authentication", StringComparison.OrdinalIgnoreCase) && line.Contains("Open", StringComparison.OrdinalIgnoreCase))
                    hasOpenAuthentication = true;
            }

            if (bssids.Count > 1 || hasOpenAuthentication)
            {
                var warning = bssids.Count > 1
                    ? $"{bssids.Count} access points advertise '{currentSsid}'. This can be normal for mesh/enterprise Wi-Fi; verify the BSSIDs."
                    : $"'{currentSsid}' reports open authentication. Verify this is expected before sending sensitive data.";
                WifiSecurityStatus.Text = warning;
                AddEvent("WI-FI REVIEW", warning);
            }
            else
            {
                WifiSecurityStatus.Text = L($"No same-name BSSID or open-network warning detected for '{currentSsid}'.");
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            AppLogger.WriteEvent("WARN", "Wi-Fi review unavailable, incomplete or exceeded its deadline.");
            WifiSecurityStatus.Text = "Wi-Fi check unavailable or exceeded its deadline.";
        }
        finally { Interlocked.Exchange(ref _wifiReviewPending, 0); }
    }

    private void ClearEventsButton_Click(object sender, RoutedEventArgs e) => EventsList.Items.Clear();

    private void OpenAlertsButton_Click(object sender, RoutedEventArgs e) => _workflow?.Select("Events");

    private void AlertsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AlertsList.SelectedItem is not ListBoxItem item || item.Tag is not string alertText)
        {
            ExplainAlertButton.IsEnabled = false;
            return;
        }
        AlertInsightEvent.Text = alertText;
        AlertInsightText.Text = T("Select Explain to get a local analysis. Ollama is used only if it is running on this PC.");
        AlertInsightSource.Text = T("Free local explainer");
        ExplainAlertButton.IsEnabled = true;
    }

    private async void ExplainAlertButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedText = (AlertsList.SelectedItem as ListBoxItem)?.Tag as string;
        var eventText = selectedText ?? _alertHistory.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(eventText))
        {
            AlertInsightText.Text = T("There are no alerts to explain.");
            return;
        }

        ExplainAlertButton.IsEnabled = false;
        AlertInsightEvent.Text = eventText;
        AlertInsightSource.Text = T("Analyzing locally...");
        AlertInsightText.Text = T("Checking likely causes and safe next steps...");
        var insight = await _eventInsight.ExplainAsync(eventText, _alertHistory.ToArray());
        AlertInsightSource.Text = insight.Source;
        AlertInsightText.Text = insight.Explanation;
        ExplainAlertButton.IsEnabled = true;
    }

    private void ClearAlertsButton_Click(object sender, RoutedEventArgs e)
    {
        AlertsList.Items.Clear();
        _alertHistory.Clear();
        AlertsTab.Header = "Alerts (0)";
        DashboardAlertBanner.Visibility = Visibility.Collapsed;
        DashboardAlertTitle.Text = T("No active alerts");
        DashboardAlertText.Text = T("Errors and security events will appear here.");
        AlertInsightEvent.Text = T("Select an alert to inspect it.");
        AlertInsightText.Text = T("The built-in diagnostics work offline.");
        ExplainAlertButton.IsEnabled = false;
    }

    private void LiveTrafficSearchBox_TextChanged(object sender, TextChangedEventArgs e) => LiveTrafficView.Refresh();

    private void LiveTrafficProtocolBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LiveTrafficView is not null) LiveTrafficView.Refresh();
    }

    private void ClearLiveTrafficFilterButton_Click(object sender, RoutedEventArgs e)
    {
        LiveTrafficSearchBox.Clear();
        LiveTrafficProtocolBox.SelectedIndex = 0;
        ClearDetailedFlowFilters();
    }

    private bool FilterLiveTraffic(object item)
    {
        if (item is not LiveTrafficRow row) return false;
        if (!FilterLiveTrafficDetails(row)) return false;
        var protocol = (LiveTrafficProtocolBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
        if (LiveTrafficProtocolBox.SelectedIndex > 0 && !string.IsNullOrWhiteSpace(protocol) && !row.Protocol.Equals(protocol, StringComparison.OrdinalIgnoreCase))
            return false;
        var search = LiveTrafficSearchBox?.Text.Trim();
        return string.IsNullOrWhiteSpace(search) || row.Application.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            row.Host.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Protocol.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            row.ExecutablePath.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateLiveTraffic(PacketObservation packet, string application, string executablePath, string host, bool outbound, int processId)
    {
        var local = outbound ? packet.Source : packet.Destination;
        var localPort = outbound ? packet.SourcePort : packet.DestinationPort;
        var remotePort = outbound ? packet.DestinationPort : packet.SourcePort;
        var key = string.Join((char)31, application, executablePath, processId.ToString(), local, localPort.ToString(), host, remotePort.ToString(), packet.Protocol);
        if (!_liveFlowByKey.TryGetValue(key, out var row))
        {
            row = new LiveTrafficRow(application, executablePath, host, packet.Protocol)
            {
                ProcessId = processId, LocalAddress = local, LocalPort = localPort,
                RemoteAddress = host, RemotePort = remotePort, FirstSeenUtc = packet.CapturedAtUtc,
                FlowKey = key, Hostname = _passiveHostnames.GetValueOrDefault(host, "Unknown")
            };
            row.Trust = _protection.GetTrust(executablePath);
            _liveFlowByKey.Add(key, row);
            LiveTrafficRows.Insert(0, row);
            if (LiveTrafficRows.Count > 2000)
            {
                var oldest = LiveTrafficRows.MinBy(flow => flow.LastSeenLocal);
                if (oldest is not null) { LiveTrafficRows.Remove(oldest); _liveFlowByKey.Remove(oldest.FlowKey); }
            }
        }
        row.AddPacket(packet.Length, outbound);
    }
    private async void StartCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        var address = GetSelectedIpv4();
        if (address is null)
        {
            PacketSummaryLabel.Text = T("Select an adapter with an IPv4 address first.");
            _lastCaptureError = PacketSummaryLabel.Text;
            RefreshCaptureDiagnostics();
            return;
        }

        if (_inspection.Snapshot.IsActive || _selectedAdapter is null) return;
        var adapterId = _selectedAdapter.Id;
        while (_pendingPackets.TryDequeue(out _)) { }
        _lastCaptureError = null;
        _workflow?.Select("Live Traffic");
        AddEvent("INFO", "Passive inspection explicitly requested; no scan started.");
        var result = await _inspection.RunAsync(adapterId, address,
            packet => _pendingPackets.TryEnqueue(packet with { CaptureAdapterId = adapterId, CaptureAddress = address.ToString() }));
        if (!result.Success) _lastCaptureError = result.Message;
        PacketSummaryLabel.Text = CaptureReadinessText.Text = result.Message;
        if (!result.Success) AddEvent("ERROR", result.Message);
        UpdateInspectionStatus();
    }

    private void StopCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        _inspection.Stop();
        PacketSummaryLabel.Text = T("Stopping capture...");
        CaptureReadinessText.Text = T("Stopping packet capture...");
        LiveTrafficStatus.Text = T("Capture stopped. Existing flow totals remain until the app closes.");
    }

    private void DrainPacketQueue()
    {
        PacketObservation? last = null;
        var drained = 0;
        var drainBudget = Stopwatch.StartNew();
        var localAddress = GetSelectedIpv4();
        var localIp = localAddress?.ToString();
        while (drained < 200 && drainBudget.Elapsed.TotalMilliseconds < 12 && _pendingPackets.TryDequeue(out var packet))
        {
            if (packet.CaptureAdapterId.Length > 0 && packet.CaptureAdapterId != _selectedAdapter?.Id) { drained++; continue; }
            if (packet.CaptureAddress.Length > 0 && packet.CaptureAddress != localIp) { drained++; continue; }
            last = packet;
            drained++;
            Interlocked.Increment(ref _packetCount);
            var bucket = packet.Length switch
            {
                < 128 => "0-127 B",
                < 512 => "128-511 B",
                < 1024 => "512-1023 B",
                _ => "1024+ B"
            };
            _packetSizeBuckets[bucket] = _packetSizeBuckets.GetValueOrDefault(bucket) + 1;
            ProcessIdentity? packetOwner = null;
            if (localAddress is not null)
            {
                var outbound = packet.Source.Equals(localIp, StringComparison.OrdinalIgnoreCase);
                var inbound = packet.Destination.Equals(localIp, StringComparison.OrdinalIgnoreCase);
                if (outbound || inbound)
                {
                    var host = outbound ? packet.Destination : packet.Source;
                    var process = packetOwner = _processOwners.Resolve(packet, localAddress);
                    var application = process?.Name ?? "Unattributed";
                    var executablePath = process?.ExecutablePath ?? string.Empty;
                    _usageStore.Record(application, executablePath, host, packet.Protocol, outbound, packet.Length, DateTime.UtcNow);
                    UpdateLiveTraffic(packet, application, executablePath, host, outbound, process?.ProcessId ?? 0);
                    if (process is not null && executablePath.Length > 0)
                        ObserveApplicationActivity(application, executablePath, host, packet.CapturedAtUtc);
                }
                foreach (var signal in _threatMonitor.Observe(packet, localIp!)) AddEvent(signal.Level, signal.Message);
            }
            _sourceTracker.Observe(packet, localIp, packetOwner?.Name ?? "Unattributed", packetOwner?.ExecutablePath ?? "");
            if (!string.IsNullOrWhiteSpace(packet.DnsName) && packet.DnsAddresses.Length > 0)
            {
                if (!_dnsAnswers.TryGetValue(packet.DnsName, out var knownAddresses))
                {
                    if (_dnsAnswers.Count >= 1024) _dnsAnswers.Remove(_dnsAnswers.Keys.First());
                    _dnsAnswers[packet.DnsName] = knownAddresses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
                foreach (var answer in packet.DnsAddresses)
                {
                    if (_passiveHostnames.Count >= 1024 && !_passiveHostnames.ContainsKey(answer)) _passiveHostnames.Remove(_passiveHostnames.Keys.First());
                    _passiveHostnames[answer] = packet.DnsName;
                    if (knownAddresses.Count >= 16) continue;
                    if (knownAddresses.Count > 0 && knownAddresses.Add(answer) && _protection.Settings.ReportDnsRotation)
                        AddEvent("DNS REVIEW", $"New address observed for {packet.DnsName}: {answer}");
                    else knownAddresses.Add(answer);
                }
            }
        }
        if (last is null) return;

        var packetCount = Interlocked.Read(ref _packetCount);
        PacketCountLabel.Text = L($"{packetCount:N0} packets");
        DashboardPacketsValue.Text = L($"{packetCount:N0} packets captured");
        PacketSummaryLabel.Text = $"Captured UTC: {last.CapturedAtUtc:O}\n{last.Source}:{last.SourcePort} → {last.Destination}:{last.DestinationPort} | {last.Protocol} | IPv4 packet length: {last.Length} bytes"
            + (last.Protocol == "TCP" ? $" | Observed TCP flags: 0x{last.TcpFlags:X2} (not a connection-state verdict)" : "")
            + $"\nHEX/ASCII shows at most the first 96 captured bytes ({last.HexPreview.Length / 2} shown), including the IPv4 header; payload and application decoding may be incomplete.";
        HexInspector.Text = last.HexPreview;
        if (_packetHex is not null)
        {
            var bytes = Convert.FromHexString(last.HexPreview);
            _packetHex.SetBytes(bytes, HexPatternEngine.Default.Analyze(bytes, HexPatternContext.Packet));
        }
        HistogramLabel.Text = string.Join(Environment.NewLine, _packetSizeBuckets.Select(pair =>
            $"{pair.Key,-12} {new string('#', Math.Min(36, pair.Value))}  {pair.Value}"));
        RefreshTopSources();
        LiveTrafficStatus.Text = L($"{LiveTrafficRows.Count:N0} flows observed since capture started. Totals reset when the app closes.");
        if (_pendingPackets.DroppedCount > 0)
        {
            LiveTrafficStatus.Text += $" Backlog samples skipped: {_pendingPackets.DroppedCount:N0}; totals and attack indicators may be incomplete.";
            if (!_reportedCaptureBacklog)
            {
                _reportedCaptureBacklog = true;
                AddEvent("WARN", "Capture exceeded the bounded analysis queue. Some packet samples were skipped to keep the interface responsive; totals and attack indicators can be incomplete.");
            }
        }
    }

    private void AddEvent(string level, string message)
    {
        var normalizedLevel = level.ToUpperInvariant();
        var isAlarm = SecurityEventPolicy.IsAlarm(level);
        var isWarning = normalizedLevel is "WARN" or "FIREWALL";
        var eventLine = $"{DateTime.Now:HH:mm:ss}  [{level}]  {message}";
        var foreground = isAlarm
            ? ThemeService.Color("FF707C")
            : isWarning
                ? ThemeService.Color("FFD26F")
                : normalizedLevel is "NEW APP" or "HOST"
                    ? ThemeService.Color("89E7AC")
                    : ThemeService.Color("CDDCD2");
        var text = new TextBlock
        {
            Text = eventLine,
            Foreground = new SolidColorBrush(foreground),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12
        };
        var background = new SolidColorBrush(isAlarm ? ThemeService.Color("200E12") : ThemeService.Color("09100B"));
        var item = new ListBoxItem
        {
            Content = text,
            Background = background,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 1, 0, 1),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Tag = eventLine
        };
        if (isAlarm)
        {
            background.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                From = ThemeService.Color("881C28"),
                To = ThemeService.Color("200E12"),
                Duration = TimeSpan.FromMilliseconds(260),
                AutoReverse = true,
                RepeatBehavior = new RepeatBehavior(3),
                FillBehavior = FillBehavior.Stop
            });
            MatrixBackground.FlashAlert();
            FlashAlarmIndicator();
        }
        EventsList.Items.Insert(0, item);
        if (isAlarm)
        {
            _alertHistory.Insert(0, eventLine);
            if (_alertHistory.Count > 100) _alertHistory.RemoveAt(_alertHistory.Count - 1);
            AlertsList.Items.Insert(0, new ListBoxItem
            {
                Tag = eventLine,
                Content = new TextBlock
                {
                    Text = eventLine,
                    Foreground = ThemeService.Brush("FF969E"),
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12
                },
                Background = ThemeService.Brush("2D0F14"),
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(0, 2, 0, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            });
            while (AlertsList.Items.Count > 200) AlertsList.Items.RemoveAt(AlertsList.Items.Count - 1);
            AlertsTab.Header = $"Alerts ({AlertsList.Items.Count})";
            DashboardAlertTitle.Text = L($"{level.ToUpperInvariant()}  /  {AlertsList.Items.Count} alert(s)");
            DashboardAlertText.Text = message;
            DashboardAlertBanner.Visibility = Visibility.Visible;
            if (AlertsList.SelectedIndex < 0) AlertsList.SelectedIndex = 0;
        }
        AppLogger.WriteEvent(level, message);
        if (isAlarm || isWarning) RefreshErrorLogPreview();
        while (EventsList.Items.Count > 500) EventsList.Items.RemoveAt(EventsList.Items.Count - 1);
    }

    private void FlashAlarmIndicator()
    {
        var restingColor = _monitoring ? ThemeService.Color("75DE88") : ThemeService.Color("DDB15B");
        var brush = new SolidColorBrush(restingColor);
        MonitorIndicator.Fill = brush;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
        {
            From = restingColor,
            To = ThemeService.Color("FF3A4A"),
            Duration = TimeSpan.FromMilliseconds(240),
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(3),
            FillBehavior = FillBehavior.Stop
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        DisposeWorkflowProcesses();
        DisposeEmailWorkflow();
        _sourceResolutionCancellation.Cancel();
        _sampleTimer.Stop();
        _connectionsTimer.Stop();
        _packetUiTimer.Stop();
        _systemTimer.Stop();
        _latencyTimer.Stop();
        _ownerRefreshTimer.Stop();
        _usageSaveTimer.Stop();
        _scanCancellation?.Cancel();
        _inspection.Dispose();
        _networkPath?.Dispose();
        _vpnPanel?.Dispose();
        _servicePanel?.Dispose();
        try { Task.Run(() => _usageStore.SaveAsync()).GetAwaiter().GetResult(); }
        catch (Exception) { }
        base.OnClosed(e);
    }

    private static bool TryGetNetwork(string value, out List<IPAddress> addresses, out string error)
    {
        addresses = [];
        error = "Enter an IPv4 CIDR range, for example 192.168.1.0/24.";
        var parts = value.Trim().Split('/', 2);
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
            !int.TryParse(parts[1], out var prefix) || prefix is < 16 or > 30) return false;
        var mask = uint.MaxValue << (32 - prefix);
        var network = ToUInt32(ip) & mask;
        var hostCount = (1UL << (32 - prefix)) - 2;
        if (hostCount > 4094) { error = "Subnet ranges are limited to 4094 hosts."; return false; }
        for (ulong offset = 1; offset <= hostCount; offset++) addresses.Add(FromUInt32(network + (uint)offset));
        error = string.Empty;
        return true;
    }

    private static uint ToUInt32(IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static IPAddress FromUInt32(uint value) => new([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]);

    private static async Task<string> ResolveDnsAsync(IPAddress address)
    {
        try { return (await Dns.GetHostEntryAsync(address).WaitAsync(TimeSpan.FromMilliseconds(700))).HostName; }
        catch (Exception) { return "-"; }
    }

    private static string ResolveMac(IPAddress address)
    {
        var physicalAddress = new byte[8];
        uint length = (uint)physicalAddress.Length;
        var result = SendARP(ToUInt32(address), 0, physicalAddress, ref length);
        return result == 0 && length >= 6 ? string.Join(":", physicalAddress.Take((int)length).Select(value => value.ToString("X2"))) : string.Empty;
    }

    [System.Runtime.InteropServices.DllImport("iphlpapi.dll", ExactSpelling = true)]
    private static extern uint SendARP(uint destinationIp, uint sourceIp, byte[] physicalAddress, ref uint physicalAddressLength);

    private static string FormatMac(byte[] bytes) => bytes.Length == 0 ? "-" : string.Join(":", bytes.Select(value => value.ToString("X2")));
    private static string FormatRate(long bytesPerSecond) => bytesPerSecond switch
    {
        >= 1_000_000 => $"{bytesPerSecond / 1_000_000d:0.00} MB/s",
        >= 1_000 => $"{bytesPerSecond / 1_000d:0.0} KB/s",
        _ => $"{bytesPerSecond:N0} B/s"
    };
    public sealed record ScanResult(string Host, string Status, string OpenPorts, string Dns);
    public sealed record ConnectionRow(string LocalAddress, string RemoteAddress, string Protocol, string State);
    private sealed record CounterSnapshot(long Received, long Sent, DateTime Timestamp);
}
