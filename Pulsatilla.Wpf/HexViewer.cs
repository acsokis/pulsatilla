using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pulsatilla.Wpf;

/// <summary>Bounded preview with recycling rows and byte-level keyboard/mouse selection.</summary>
public sealed class HexViewer : UserControl
{
    private readonly ListBox _rows = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, FontFamily = new FontFamily("Consolas"), FontSize = 12 };
    private readonly TextBlock _details = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
    private byte[] _sample = [];
    private HexAnalysisResult? _analysis;
    public int SelectedOffset { get; private set; } = -1;
    public HexViewer()
    {
        FlowDirection = FlowDirection.LeftToRight;
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new TextBlock { Text = "Offset       HEX bytes                                        ASCII", FontFamily = new FontFamily("Consolas"), Margin = new Thickness(2, 2, 0, 4) };
        var detailScroll = new ScrollViewer { Content = _details, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        grid.Children.Add(header); Grid.SetRow(_rows, 1); grid.Children.Add(_rows); Grid.SetRow(detailScroll, 2); grid.Children.Add(detailScroll); Content = grid;
        SetResourceReference(ForegroundProperty, "Theme_E6F2E8"); _rows.SetResourceReference(BackgroundProperty, "Theme_0C1810"); _rows.SetResourceReference(ForegroundProperty, "Theme_E6F2E8");
        VirtualizingPanel.SetIsVirtualizing(_rows, true); VirtualizingPanel.SetVirtualizationMode(_rows, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(_rows, true); ScrollViewer.SetHorizontalScrollBarVisibility(_rows, ScrollBarVisibility.Auto);
        var template = new DataTemplate(typeof(HexRow));
        var row = new FrameworkElementFactory(typeof(HexRowVisual)); row.SetBinding(HexRowVisual.RowProperty, new System.Windows.Data.Binding("."));
        template.VisualTree = row; _rows.ItemTemplate = template;
        _rows.SelectionChanged += (_, _) => { if (_rows.SelectedItem is HexRow selected) SelectByte(selected.Offset); };
        _rows.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var node = e.OriginalSource as DependencyObject;
            while (node is not null && node is not HexRowVisual) node = VisualTreeHelper.GetParent(node);
            if (node is HexRowVisual visual && visual.Row is { } r)
            {
                var x = e.GetPosition(visual).X; var byteIndex = Math.Clamp((int)((x - 90) / 25), 0, 15);
                _rows.SelectedIndex = r.Offset / 16;
                SelectByte(Math.Min(r.Offset + byteIndex, _sample.Length - 1));
                e.Handled = true;
            }
        };
        _rows.PreviewKeyDown += (_, e) =>
        {
            var delta = e.Key switch { System.Windows.Input.Key.Left => -1, System.Windows.Input.Key.Right => 1, System.Windows.Input.Key.Up => -16, System.Windows.Input.Key.Down => 16, _ => 0 };
            if (delta == 0 || _sample.Length == 0) return; SelectByte(Math.Clamp(SelectedOffset + delta, 0, _sample.Length - 1)); e.Handled = true;
        };
    }
    public void SetBytes(ReadOnlySpan<byte> sample, HexAnalysisResult? analysis = null)
    {
        _sample = sample[..Math.Min(sample.Length, HexPatternEngine.MaximumSampleBytes)].ToArray(); _analysis = analysis; SelectedOffset = -1;
        _rows.ItemsSource = Enumerable.Range(0, (_sample.Length + 15) / 16).Select(index => new HexRow(index * 16,
            _sample.Skip(index * 16).Take(16).ToArray(), analysis?.Matches.Where(m => m.Offset < index * 16 + 16 && m.Offset + m.Length > index * 16).ToArray() ?? [])).ToArray();
        _details.Text = $"Preview {_sample.Length:N0} bytes. " + (sample.Length > _sample.Length || analysis?.Truncated == true ? "Bounded/truncated. " : "") + (analysis?.Interpretation ?? "Select a byte to inspect its offset.");
    }
    private void SelectByte(int offset)
    {
        if (offset < 0 || offset >= _sample.Length) return; SelectedOffset = offset;
        var matches = _analysis?.Matches.Where(m => offset >= m.Offset && offset < m.Offset + m.Length).ToArray() ?? [];
        _details.Text = $"Selected offset 0x{offset:X8}: {_sample[offset]:X2} ({_sample[offset]})\n" + string.Join("\n", matches.Take(4).Select(m => $"{m.Category}: {m.Name} — {m.Description[..Math.Min(m.Description.Length, 200)]}")) + (matches.Length > 4 ? $"\n{matches.Length - 4} additional rules match this byte." : "");
        if (_rows.Items.Count > offset / 16) _rows.ScrollIntoView(_rows.Items[offset / 16]);
        InvalidateRows(_rows);
    }
    private static void InvalidateRows(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        { var child = VisualTreeHelper.GetChild(parent, i); if (child is HexRowVisual row) row.InvalidateVisual(); else InvalidateRows(child); }
    }
    public sealed record HexRow(int Offset, byte[] Bytes, IReadOnlyList<HexPatternMatch> Matches);
    public sealed class HexRowVisual : FrameworkElement
    {
        public static readonly DependencyProperty RowProperty = DependencyProperty.Register(nameof(Row), typeof(HexRow), typeof(HexRowVisual), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public HexRow? Row { get => (HexRow?)GetValue(RowProperty); set => SetValue(RowProperty, value); }
        protected override Size MeasureOverride(Size availableSize) => new(650, 22);
        protected override void OnRender(DrawingContext dc)
        {
            if (Row is not { } r) return;
            var brush = TryFindResource("Theme_E6F2E8") as Brush ?? Brushes.White;
            var highlight = TryFindResource("Theme_305C42") as Brush ?? Brushes.DarkGreen;
            DependencyObject? ancestor = this;
            while (ancestor is not null && ancestor is not HexViewer) ancestor = VisualTreeHelper.GetParent(ancestor);
            var selected = (ancestor as HexViewer)?.SelectedOffset ?? -1;
            void Draw(string text, double x) => dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Consolas"), 12, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), new Point(x, 3));
            Draw($"{r.Offset:X8}", 2);
            for (var b = 0; b < r.Bytes.Length; b++)
            {
                if (r.Matches.Any(m => r.Offset + b >= m.Offset && r.Offset + b < m.Offset + m.Length)) dc.DrawRectangle(highlight, null, new Rect(88 + b * 25, 0, 23, 21));
                Draw(r.Bytes[b].ToString("X2"), 90 + b * 25);
                if (r.Offset + b == selected) dc.DrawRectangle(null, new Pen(brush, 1.5), new Rect(88 + b * 25, 0, 23, 21));
            }
            Draw(new string(r.Bytes.Select(b => b is >= 32 and <= 126 ? (char)b : '.').ToArray()), 503);
        }
    }
}
