using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace Pulsatilla.Wpf;

public sealed record TrafficPoint(DateTime TimeUtc, double Download, double Upload, double IntervalSeconds);

/// <summary>Bounded 15-minute history with chart geometry cached between samples.</summary>
public sealed class VisualTrafficChart : FrameworkElement
{
    private const int Capacity = 1800;
    private readonly Queue<TrafficPoint> _history = new(Capacity);
    private TrafficPoint[]? _frozenHistory;
    private TrafficPoint[] _visible = [];
    private DrawingGroup? _drawing;
    private Rect _plot;
    private DateTime _end;
    private double _scale = 1024, _windowSeconds = 60;
    private int _hoverIndex = -1;
    private bool _showDownload = true, _showUpload = true;
    private static readonly Typeface LabelTypeface = new("Segoe UI");
    private static SolidColorBrush DownloadBrush => FrozenBrush(ThemeService.IsLight ? "9B5F00" : "F6BE52");
    private static SolidColorBrush UploadBrush => FrozenBrush(ThemeService.IsLight ? "7043B8" : "AC91FA");

    public VisualTrafficChart()
    {
        FlowDirection = FlowDirection.LeftToRight; ClipToBounds = true;
        Loaded += (_, _) => { ThemeService.Changed += OnThemeChanged; Rebuild(); };
        Unloaded += (_, _) => ThemeService.Changed -= OnThemeChanged;
        IsVisibleChanged += (_, _) => { if (IsVisible) Rebuild(); };
        MouseLeave += (_, _) => { _hoverIndex = -1; InvalidateVisual(); };
    }
    public bool IsFrozen
    {
        get => _frozenHistory is not null;
        set { if (value == IsFrozen) return; _frozenHistory = value ? _history.ToArray() : null; Rebuild(); }
    }
    public double WindowSeconds
    {
        get => _windowSeconds;
        set { _windowSeconds = Math.Clamp(double.IsFinite(value) ? value : 60, 60, 900); Rebuild(); }
    }
    public bool ShowDownload { get => _showDownload; set { if (value == _showDownload) return; _showDownload = value; Rebuild(); } }
    public bool ShowUpload { get => _showUpload; set { if (value == _showUpload) return; _showUpload = value; Rebuild(); } }
    internal int SampleCount => _history.Count;
    internal double DisplayScale => _scale;
    internal IReadOnlyList<TrafficPoint> VisiblePoints => _visible;
    public string Summary
    {
        get
        {
            double download = 0, upload = 0;
            var start = _end.AddSeconds(-WindowSeconds);
            foreach (var point in _visible)
            {
                var seconds = Math.Min(point.IntervalSeconds, Math.Max(0, (point.TimeUtc - start).TotalSeconds));
                download += point.Download * seconds; upload += point.Upload * seconds;
            }
            var peak = _visible.Length == 0 ? 0 : _visible.Max(point => point.Download + point.Upload);
            return $"Window volume: ↓ {TrafficFormat.Bytes(download)}  ↑ {TrafficFormat.Bytes(upload)}  ·  Combined peak: {TrafficFormat.Rate(peak)}";
        }
    }
    public void AddSample(double download, double upload, DateTime? timeUtc = null)
    {
        var time = (timeUtc ?? DateTime.UtcNow).ToUniversalTime();
        if (_history.Count > 0 && time <= _history.Last().TimeUtc) return;
        static double Rate(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1_000_000_000_000) : 0;
        var interval = _history.Count > 0 ? (time - _history.Last().TimeUtc).TotalSeconds : 0;
        if (_history.Count == Capacity) _history.Dequeue();
        _history.Enqueue(new(time, Rate(download), Rate(upload), interval));
        if (!IsFrozen) Rebuild();
    }
    public void Clear() { _history.Clear(); _frozenHistory = null; _hoverIndex = -1; Rebuild(); }
    private void OnThemeChanged(object? sender, EventArgs e) => Rebuild();
    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo) { base.OnRenderSizeChanged(sizeInfo); Rebuild(); }
    private void Rebuild()
    {
        var history = _frozenHistory ?? _history.ToArray();
        _end = history.Length > 0 ? history[^1].TimeUtc : DateTime.UtcNow;
        _visible = history.Where(point => point.TimeUtc >= _end.AddSeconds(-WindowSeconds)).ToArray();
        _hoverIndex = Math.Min(_hoverIndex, _visible.Length - 1);
        _plot = new Rect(66, 14, Math.Max(1, ActualWidth - 82), Math.Max(1, ActualHeight - 47));
        var peak = _visible.Length == 0 ? 0 : _visible.Max(point => Math.Max(ShowDownload ? point.Download : 0, ShowUpload ? point.Upload : 0));
        _scale = NiceScale(Math.Max(1024, peak * 1.12));
        if (!IsVisible) { _drawing = null; return; }
        var group = new DrawingGroup();
        using (var context = group.Open())
        {
            var grid = FrozenPen(ThemeService.Color("31513B"), 1, .35);
            var label = FrozenBrush(ThemeService.Color("91A69A"));
            for (var row = 0; row <= 4; row++)
            {
                var y = _plot.Top + row * _plot.Height / 4;
                context.DrawLine(grid, new(_plot.Left, y), new(_plot.Right, y));
                Label(context, TrafficFormat.Rate(_scale * (1 - row / 4d)), label, new(0, Math.Max(0, y - 7)), 10);
            }
            for (var column = 0; column <= 4; column++)
            {
                var x = _plot.Left + column * _plot.Width / 4;
                context.DrawLine(grid, new(x, _plot.Top), new(x, _plot.Bottom));
                var time = _end.AddSeconds(-WindowSeconds + column * WindowSeconds / 4).ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                Label(context, time, label, new(Math.Clamp(x - 24, _plot.Left, Math.Max(_plot.Left, _plot.Right - 50)), _plot.Bottom + 9), 10);
            }
            if (ShowDownload) DrawSeries(context, true, DownloadBrush);
            if (ShowUpload) DrawSeries(context, false, UploadBrush);
            if (_visible.Length == 0) Label(context, "Waiting for live adapter traffic…", label, new(_plot.Left + 15, _plot.Top + 25), 12);
        }
        group.Freeze(); _drawing = group; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        context.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_drawing is not null) context.DrawDrawing(_drawing);
        if (_hoverIndex < 0 || _hoverIndex >= _visible.Length) return;
        var point = _visible[_hoverIndex]; var x = X(point.TimeUtc);
        var cursor = FrozenPen(ThemeService.Color("91A69A"), 1, .75);
        context.DrawLine(cursor, new(x, _plot.Top), new(x, _plot.Bottom));
        if (ShowDownload) context.DrawEllipse(DownloadBrush, null, new(x, Y(point.Download)), 3.5, 3.5);
        if (ShowUpload) context.DrawEllipse(UploadBrush, null, new(x, Y(point.Upload)), 3.5, 3.5);
        var box = new Rect(Math.Clamp(x + 12, _plot.Left, Math.Max(_plot.Left, _plot.Right - 218)), _plot.Top + 6, 214, 66);
        context.DrawRoundedRectangle(FrozenBrush(ThemeService.Color("0C1810")), cursor, box, 4, 4);
        Label(context, point.TimeUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture), FrozenBrush(ThemeService.Color("E6F2E8")), new(box.Left + 10, box.Top + 7), 11);
        Label(context, "↓ " + TrafficFormat.Rate(point.Download), DownloadBrush, new(box.Left + 10, box.Top + 25), 11);
        Label(context, "↑ " + TrafficFormat.Rate(point.Upload), UploadBrush, new(box.Left + 10, box.Top + 43), 11);
    }
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        var index = _plot.Contains(position) ? FindNearest(position.X) : -1;
        if (index == _hoverIndex) return; _hoverIndex = index; InvalidateVisual();
    }
    private int FindNearest(double x)
    {
        if (_visible.Length == 0) return -1;
        var time = _end.AddSeconds(-WindowSeconds + Math.Clamp((x - _plot.Left) / _plot.Width, 0, 1) * WindowSeconds);
        var low = 0; var high = _visible.Length - 1;
        while (low < high) { var middle = (low + high) / 2; if (_visible[middle].TimeUtc < time) low = middle + 1; else high = middle; }
        return low > 0 && Math.Abs((_visible[low - 1].TimeUtc - time).Ticks) < Math.Abs((_visible[low].TimeUtc - time).Ticks) ? low - 1 : low;
    }
    private void DrawSeries(DrawingContext context, bool download, SolidColorBrush color)
    {
        if (_visible.Length == 0) return;
        var pen = new Pen(color, 1.8); pen.Freeze();
        var fill = color.Clone(); fill.Opacity = download ? .32 : .24; fill.Freeze();
        var start = 0;
        for (var index = 1; index <= _visible.Length; index++)
        {
            if (index < _visible.Length && (_visible[index].TimeUtc - _visible[index - 1].TimeUtc).TotalSeconds <= 2) continue;
            var first = _visible[start]; var last = _visible[index - 1];
            var area = new StreamGeometry(); var line = new StreamGeometry();
            using (var a = area.Open()) using (var l = line.Open())
            {
                a.BeginFigure(new(X(first.TimeUtc), _plot.Bottom), true, true);
                l.BeginFigure(new(X(first.TimeUtc), Y(download ? first.Download : first.Upload)), false, false);
                for (var sample = start; sample < index; sample++)
                {
                    var p = _visible[sample]; var coordinate = new Point(X(p.TimeUtc), Y(download ? p.Download : p.Upload));
                    a.LineTo(coordinate, true, false); if (sample > start) l.LineTo(coordinate, true, false);
                }
                a.LineTo(new(X(last.TimeUtc), _plot.Bottom), true, false);
            }
            area.Freeze(); line.Freeze(); context.DrawGeometry(fill, null, area); context.DrawGeometry(null, pen, line);
            start = index;
        }
        var latest = _visible[^1]; context.DrawEllipse(color, null, new(X(latest.TimeUtc), Y(download ? latest.Download : latest.Upload)), 3, 3);
    }
    private double X(DateTime time) => _plot.Right - (_end - time).TotalSeconds / WindowSeconds * _plot.Width;
    private double Y(double rate) => _plot.Bottom - Math.Clamp(rate / _scale, 0, 1) * _plot.Height;
    private void Label(DrawingContext context, string text, Brush brush, Point point, double size) => context.DrawText(
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, LabelTypeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);
    private static double NiceScale(double value) => Math.Pow(2, Math.Ceiling(Math.Log2(value)));
    private static SolidColorBrush FrozenBrush(string hex) => FrozenBrush((Color)ColorConverter.ConvertFromString("#" + hex));
    private static SolidColorBrush FrozenBrush(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
    private static Pen FrozenPen(Color color, double width, double opacity) { var brush = new SolidColorBrush(color) { Opacity = opacity }; brush.Freeze(); var pen = new Pen(brush, width); pen.Freeze(); return pen; }
}
