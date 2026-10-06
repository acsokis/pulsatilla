using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Controls;

namespace Pulsatilla.Wpf;

public sealed class ServiceBannerPanel : UserControl, IDisposable
{
    private readonly ActiveServiceScanner _scanner = new();
    private readonly TextBox _address = new() { MinWidth = 160, MaxWidth = 500, Margin = new Thickness(4) };
    private readonly TextBox _port = new() { Text = "22", Width = 76, Margin = new Thickness(4) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
    private readonly FileInspectionPanel _sample = new();
    private readonly Button _start = new() { Content = "Inspect service banner", Margin = new Thickness(4), Padding = new Thickness(10, 6, 10, 6) };
    private readonly Button _cancel = new() { Content = "Cancel", Margin = new Thickness(4), IsEnabled = false };
    private CancellationTokenSource? _operation;
    private bool _disposed;
    public ServiceBannerPanel()
    {
        var grid = new Grid { Margin = new Thickness(10) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        var description = new TextBlock { Text = "ACTIVE NETWORK OPERATION — one explicitly selected TCP endpoint. Receive-only service identification: no request payload is sent, and many services will not provide a banner. Five-second deadline, 16 KiB sample limit. Samples are displayed as bytes and are never executed or opened as a web page.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6) };
        var actions = new WrapPanel(); actions.Children.Add(new TextBlock { Text = "Literal IP", VerticalAlignment = VerticalAlignment.Center }); actions.Children.Add(_address);
        actions.Children.Add(new TextBlock { Text = "TCP port", VerticalAlignment = VerticalAlignment.Center }); actions.Children.Add(_port); actions.Children.Add(_start); actions.Children.Add(_cancel);
        grid.Children.Add(description); Grid.SetRow(actions, 1); grid.Children.Add(actions); Grid.SetRow(_status, 2); grid.Children.Add(_status); Grid.SetRow(_sample, 3); grid.Children.Add(_sample);
        Content = grid; SetResourceReference(ForegroundProperty, "Theme_E6F2E8");
        _status.Text = "NOT STARTED. Choose an endpoint you are authorized to inspect; no network connection starts when this page opens.";
        _start.Click += StartClick; _cancel.Click += (_, _) => _operation?.Cancel(); Unloaded += (_, _) => _operation?.Cancel();
    }
    public void SetTarget(string ip) { if (!_disposed && _operation is null) _address.Text = ip[..Math.Min(ip.Length, 100)]; }
    private async void StartClick(object sender, RoutedEventArgs e)
    {
        if (_disposed || _operation is not null) return;
        if (!int.TryParse(_port.Text.Trim(), out var port)) { _status.Text = "Enter a TCP port from 1 to 65535."; return; }
        string target;
        try { target = ActiveServiceScanner.ValidateTarget(_address.Text, port).ToString(); }
        catch (ArgumentException ex) { _status.Text = ex.Message; return; }
        if (MessageBox.Show(Window.GetWindow(this), $"Connect to {target}:{port} and read a TCP service banner? This is an active network operation; inspect only endpoints you are authorized to use. No protocol payload is sent.", "Explicit service inspection", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        var operation = _operation = new CancellationTokenSource(); _start.IsEnabled = false; _cancel.IsEnabled = true;
        _address.IsEnabled = false; _port.IsEnabled = false; _status.Text = $"Connecting to {target}:{port}; receiving at most 16 KiB for up to five seconds…";
        try
        {
            var result = await _scanner.InspectAsync(target, port, operation.Token);
            if (_disposed) return;
            _sample.SetSample(result.Bytes, $"TCP banner from {result.Address}:{result.Port}", HexPatternContext.Scanner);
            _status.Text = $"{result.State} — {result.Bytes.Length:N0} bytes received, {result.Elapsed.TotalSeconds:F1} seconds. " +
                (result.State == ServiceBannerState.PreviewLimitReached ? "Preview limit reached; additional bytes may exist." : result.State == ServiceBannerState.DeadlineReached ? "The connection was closed at the deadline; any received partial banner remains available." : "No file or service content was executed.");
        }
        catch (OperationCanceledException) { if (!_disposed) _status.Text = "Service inspection cancelled; the owned connection was disposed."; }
        catch (Exception ex) when (ex is SocketException or IOException or ArgumentException)
        { if (!_disposed) _status.Text = "The selected endpoint could not provide a service banner; the owned connection was closed."; }
        finally
        {
            operation.Dispose(); _operation = null;
            if (!_disposed) { _start.IsEnabled = true; _cancel.IsEnabled = false; _address.IsEnabled = true; _port.IsEnabled = true; }
        }
    }
    public void Dispose() { _disposed = true; _operation?.Cancel(); }
}
