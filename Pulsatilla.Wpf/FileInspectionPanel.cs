using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Pulsatilla.Wpf;

/// <summary>Shared explicit local sample/application review. Files are read, never launched.</summary>
public sealed class FileInspectionPanel : UserControl
{
    public event Action<FileInspection>? InspectionCompleted;
    private readonly LocalFileInspector _inspector = new();
    private readonly Func<string, CancellationToken, Task<FileInspection>> _inspect;
    private readonly MicrosoftDefenderProvider _defender = new();
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly HexViewer _hex = new() { MinHeight = 60 };
    private readonly ListBox _matches = new() { MaxHeight = 90 };
    private readonly Button _scan = new() { Content = "Scan with Microsoft Defender", IsEnabled = false, Margin = new Thickness(4) };
    private CancellationTokenSource? _operation;
    private string? _path;
    private string? _hash;
    public string CurrentDetails => _details.Text;
    public FileInspectionPanel(Func<string, CancellationToken, Task<FileInspection>>? inspect = null)
    {
        _inspect = inspect ?? _inspector.InspectAsync;
        var grid = new Grid { Margin = new Thickness(12) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var actions = new WrapPanel();
        var choose = new Button { Content = "Choose local file / application", Margin = new Thickness(4) };
        var copy = new Button { Content = "Copy SHA-256", Margin = new Thickness(4) };
        var vt = new Button { Content = "Open VirusTotal hash page", Margin = new Thickness(4) };
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(4) };
        actions.Children.Add(choose); actions.Children.Add(copy); actions.Children.Add(vt); actions.Children.Add(_scan); actions.Children.Add(cancel);
        var detailsScroll = new ScrollViewer { Content = _details, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        grid.Children.Add(actions); Grid.SetRow(detailsScroll, 1); grid.Children.Add(detailsScroll); Grid.SetRow(_hex, 2); grid.Children.Add(_hex); Grid.SetRow(_matches, 3); grid.Children.Add(_matches);
        Content = grid; SetResourceReference(ForegroundProperty, "Theme_E6F2E8");
        _details.Text = "Choose a local file for bounded SHA-256, embedded signature and byte-pattern inspection. Nothing is executed or uploaded. Local risk is advisory.";
        choose.Click += async (_, _) => { var dialog = new OpenFileDialog { CheckFileExists = true }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) await InspectPathAsync(dialog.FileName); };
        copy.Click += (_, _) => { if (_hash is not null) Clipboard.SetText(_hash); };
        vt.Click += (_, _) =>
        {
            if (_hash is null || MessageBox.Show(Window.GetWindow(this), "Open VirusTotal in your browser? Only the selected SHA-256 will be sent to that website. No file is uploaded by Pulsatilla.", "External hash lookup", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://www.virustotal.com/gui/file/" + _hash) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { _details.Text += "\nBrowser could not be opened."; }
        };
        cancel.Click += (_, _) => { InvalidateOperation(); _scan.IsEnabled = _path is not null; _details.Text += "\nOperation cancelled; late results are ignored. For Defender, check Windows Security scan state."; };
        _scan.Click += async (_, _) =>
        {
            if (_path is null || MessageBox.Show(Window.GetWindow(this), "Run a Microsoft Defender custom scan of this file? Pulsatilla does not upload it or change Defender settings. Defender may use its configured cloud protection. Remediation is disabled for this custom scan; review results in Windows Security.\n\n" + _path,
                "Explicit Defender scan", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            var operation = StartOperation(); _scan.IsEnabled = false;
            try { var result = await _defender.InspectAsync(_path, true, operation.Token); if (IsCurrent(operation)) _details.Text += "\n" + result.State + ": " + result.Summary; }
            catch (OperationCanceledException) { if (IsCurrent(operation)) _details.Text += "\nScan wait cancelled; check Windows Security."; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or ArgumentException) { if (IsCurrent(operation)) _details.Text += "\nDefender could not scan the selected file."; }
            finally { if (IsCurrent(operation)) _scan.IsEnabled = _path is not null; CompleteOperation(operation); }
        };
        Unloaded += (_, _) => InvalidateOperation();
    }
    private void InvalidateOperation()
    {
        var old = _operation; _operation = null;
        try { old?.Cancel(); } catch (AggregateException) { /* Result generation is invalid even if a cancellation observer fails. */ }
    }
    private CancellationTokenSource StartOperation() { InvalidateOperation(); return _operation = new CancellationTokenSource(); }
    private bool IsCurrent(CancellationTokenSource operation) => ReferenceEquals(operation, _operation) && !operation.IsCancellationRequested;
    private void CompleteOperation(CancellationTokenSource operation) { if (ReferenceEquals(operation, _operation)) _operation = null; operation.Dispose(); }
    public async Task InspectPathAsync(string path)
    {
        var operation = StartOperation(); _path = null; _hash = null; _scan.IsEnabled = false; _details.Text = "Inspecting locally…";
        _hex.SetBytes([]); _matches.ItemsSource = Array.Empty<string>();
        try
        {
            var result = await _inspect(path, operation.Token);
            if (!IsCurrent(operation)) return;
            _path = result.Path; _hash = result.Sha256; _scan.IsEnabled = true;
            _details.Text = $"{result.Path}\n{result.Size:N0} bytes • Modified {result.ModifiedUtc:u}\nSHA-256: {result.Sha256}\nSignature: {result.Signature.Status} • Publisher: {result.Signature.Publisher}\n{result.Signature.Details}\nLocal risk: {result.Risk} (advisory)\n" + string.Join("\n", result.Reasons);
            _hex.SetBytes(result.Preview, result.Analysis); _matches.ItemsSource = result.Analysis.Matches.Select(m => $"0x{m.Offset:X}: {m.Category} / {m.Name} / {m.Severity}");
            InspectionCompleted?.Invoke(result);
        }
        catch (OperationCanceledException) { if (IsCurrent(operation)) _details.Text = "Local inspection cancelled."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException) { if (IsCurrent(operation)) _details.Text = "The selected local file could not be inspected. It may be inaccessible, changed, oversized or a reparse/network path."; }
        finally { CompleteOperation(operation); }
    }
    public void SetSample(byte[] bytes, string label, HexPatternContext context = HexPatternContext.Scanner)
    {
        InvalidateOperation(); _path = null; _hash = null; _scan.IsEnabled = false;
        var sample = bytes.AsSpan(0, Math.Min(bytes.Length, HexPatternEngine.MaximumSampleBytes));
        var analysis = HexPatternEngine.Default.Analyze(sample, context); _hex.SetBytes(sample, analysis);
        _details.Text = $"{label[..Math.Min(label.Length, 1024)]}: {sample.Length:N0} of {bytes.Length:N0} bytes shown. " + analysis.Interpretation;
        _matches.ItemsSource = analysis.Matches.Select(m => $"0x{m.Offset:X}: {m.Category} / {m.Name} / {m.Severity}");
    }
}
