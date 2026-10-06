using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Pulsatilla.Wpf;

public sealed class VpnPanel : UserControl, IDisposable
{
    private readonly DpapiVpnProfileStore _store = new();
    private readonly OpenVpnProvider _provider;
    private readonly ComboBox _profiles = new() { MinWidth = 180, DisplayMemberPath = "Name", Margin = new Thickness(0, 6, 0, 6) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _network = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock _privacy = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly Button _connect;
    private readonly Button _disconnect;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private bool _disposed;
    private string? _previousPublicIp;
    public event EventHandler? OpenRoutesRequested;
    public VpnPanel()
    {
        _provider = new OpenVpnProvider(_store); _provider.StatusChanged += ProviderChanged;
        var stack = new StackPanel { Margin = new Thickness(14), MaxWidth = 1100, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(new TextBlock { Text = "VPN — provider-neutral OpenVPN profiles", FontSize = 20, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = "EXPERIMENTAL connection control. Profile import is available; a verified OpenVPN Community executable and administrator rights are required to connect. Certificate-only profiles are supported. Password/MFA/encrypted-key authentication, kill switch and split routing are not implemented.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 8) });
        stack.Children.Add(_profiles);
        var actions = new WrapPanel();
        AddButton(actions, "Import .ovpn", ImportClick); AddButton(actions, "Remove selected profile", RemoveClick);
        _connect = AddButton(actions, "Connect (experimental)", ConnectClick);
        _disconnect = AddButton(actions, "Disconnect owned session", DisconnectClick);
        AddButton(actions, "Refresh local status", (_, _) => Refresh());
        AddButton(actions, "Network / Routes", (_, _) => OpenRoutesRequested?.Invoke(this, EventArgs.Empty));
        stack.Children.Add(actions); stack.Children.Add(_status);
        stack.Children.Add(new TextBlock { Text = "Local adapters, addresses and configured DNS servers", FontSize = 16, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(_network);
        var privacyActions = new WrapPanel(); AddButton(privacyActions, "Check public IP (external HTTPS request)", PrivacyClick);
        stack.Children.Add(privacyActions); stack.Children.Add(_privacy);
        stack.Children.Add(new TextBlock { Text = "The public-IP action contacts api.ipify.org only when requested. Adapter addresses and configured DNS do not prove which resolver actually handles a query. Check the route table and IPv6 separately. A VPN does not establish anonymity. Pulsatilla supplies no VPN bandwidth; provider accounts and profiles are supplied by you.", TextWrapping = TextWrapping.Wrap });
        SetResourceReference(ForegroundProperty, "Theme_E2F3E7");
        Content = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        _profiles.SelectionChanged += (_, _) => UpdateButtons(); Loaded += (_, _) => Refresh();
    }
    private static Button AddButton(Panel panel, string text, RoutedEventHandler handler)
    { var button = new Button { Content = text, Margin = new Thickness(0, 4, 8, 4), Padding = new Thickness(10, 6, 10, 6) }; button.Click += handler; panel.Children.Add(button); return button; }
    private void ProviderChanged(object? sender, EventArgs e)
    { if (!_disposed) Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) RefreshStatus(); })); }
    private void Refresh()
    {
        try
        {
            var selected = (_profiles.SelectedItem as VpnProfile)?.Id;
            var profiles = _store.List(); _profiles.ItemsSource = profiles;
            _profiles.SelectedItem = profiles.FirstOrDefault(p => p.Id == selected) ?? profiles.FirstOrDefault();
            var lines = new List<string>();
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(a => a.OperationalStatus == OperationalStatus.Up).Take(32))
            {
                var properties = adapter.GetIPProperties();
                var addresses = string.Join(", ", properties.UnicastAddresses.Take(8).Select(a => a.Address.ToString()));
                var dns = string.Join(", ", properties.DnsAddresses.Take(8));
                var hint = adapter.NetworkInterfaceType == NetworkInterfaceType.Tunnel ? "Tunnel interface type" : "Tunnel/underlay relationship: inspect Routes";
                lines.Add($"{adapter.Name} | {hint}\nIP: {addresses}\nConfigured DNS: {dns}");
            }
            _network.Text = lines.Count == 0 ? "No operational adapters reported." : string.Join("\n\n", lines);
            RefreshStatus();
        }
        catch (Exception) { _status.Text = "Local VPN profile or adapter data could not be read. No connection was started."; UpdateButtons(); }
    }
    private void RefreshStatus()
    {
        var status = _provider.Status;
        var installed = OpenVpnProvider.DetectInstalledExecutable() is not null;
        _status.Text = $"OpenVPN: {(installed ? "DETECTED; signature is checked before launch" : "NOT INSTALLED in the supported location")}\nOwned session: {status.State}\n{status.Message}" +
            (status.ConnectedUtc.HasValue ? $"\nConnected since {status.ConnectedUtc.Value.LocalDateTime:g}.\nAssigned tunnel IP: {status.TunnelAddress}\nMatching interface: {status.TunnelInterface}\nPhysical underlay: inspect Network / Routes; not inferred here." : "");
        UpdateButtons();
    }
    private void UpdateButtons()
    {
        _connect.IsEnabled = !_busy && !_provider.Status.OwnedByPulsatilla && _profiles.SelectedItem is VpnProfile { RequiresPassword: false } && OpenVpnProvider.DetectInstalledExecutable() is not null;
        _disconnect.IsEnabled = !_busy && _provider.Status.OwnedByPulsatilla;
    }
    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var dialog = new OpenFileDialog { Filter = "OpenVPN profile (*.ovpn)|*.ovpn", CheckFileExists = true };
        if (dialog.ShowDialog() != true) return;
        _busy = true; UpdateButtons();
        try { await _store.ImportAsync(dialog.FileName, _lifetime.Token); Refresh(); _status.Text += "\nProfile protected with Windows DPAPI for the current user."; }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { _status.Text = ex is InvalidDataException ? ex.Message : "The profile could not be imported or protected. No VPN connection was started."; }
        finally { _busy = false; UpdateButtons(); }
    }
    private void RemoveClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _profiles.SelectedItem is not VpnProfile profile) return;
        if (_provider.Status.OwnedByPulsatilla) { _status.Text = "Disconnect the owned VPN session before removing profiles."; return; }
        if (MessageBox.Show("Remove this protected local profile? The original .ovpn file is retained.", "Pulsatilla VPN", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        try { _store.Remove(profile.Id); new VpnCredentialStore().Remove(profile.Id); Refresh(); }
        catch (Exception) { _status.Text = "The protected profile could not be removed."; }
    }
    private async void ConnectClick(object sender, RoutedEventArgs e)
    {
        if (_busy || _profiles.SelectedItem is not VpnProfile profile) return;
        if (MessageBox.Show("Start the installed OpenVPN executable with this reviewed profile? OpenVPN may change network adapters, routes and DNS. The integration is experimental and has no kill switch. Check routes after disconnecting.", "Connect VPN", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _busy = true; UpdateButtons();
        try { await _provider.ConnectAsync(profile, _lifetime.Token); Refresh(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException or TimeoutException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        { _status.Text = ex is InvalidOperationException ? ex.Message : "OpenVPN could not establish the session; no password or provider output was logged."; }
        finally { _busy = false; UpdateButtons(); }
    }
    private async void DisconnectClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true; UpdateButtons();
        try { await _provider.DisconnectAsync(_lifetime.Token); Refresh(); }
        catch (OperationCanceledException) { }
        finally { _busy = false; UpdateButtons(); }
    }
    private async void PrivacyClick(object sender, RoutedEventArgs e)
    {
        if (_busy) return; _busy = true;
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token); deadline.CancelAfter(TimeSpan.FromSeconds(6));
            using var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
            using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            using var response = await client.GetAsync("https://api.ipify.org", HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token); var bytes = new byte[257]; var count = 0;
            while (count < bytes.Length) { var read = await stream.ReadAsync(bytes.AsMemory(count), deadline.Token); if (read == 0) break; count += read; }
            if (count > 256 || !IPAddress.TryParse(System.Text.Encoding.ASCII.GetString(bytes, 0, count).Trim(), out var address)) throw new IOException("Invalid public-IP response.");
            var current = address.ToString(); _privacy.Text = $"Observed public IP: {current}" + (_previousPublicIp is null ? "\nBaseline saved in memory for this panel only." : current == _previousPublicIp ? "\nPublic IP unchanged from the previous explicit check." : "\nPublic IP changed from the previous explicit check; this is not proof of anonymity.");
            _previousPublicIp = current;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException) { _privacy.Text = "The explicitly requested public-IP check failed or timed out."; }
        finally { _busy = false; UpdateButtons(); }
    }
    public void Dispose()
    {
        if (_disposed) return; _disposed = true; _lifetime.Cancel(); _provider.StatusChanged -= ProviderChanged;
        _ = _provider.DisposeAsync().AsTask();
    }
}
