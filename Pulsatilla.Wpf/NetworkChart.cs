using System.Windows;
using System.Windows.Media;

namespace Pulsatilla.Wpf;

public sealed class NetworkChart : FrameworkElement
{
    private readonly Queue<double> _received = new();
    private readonly Queue<double> _sent = new();
    private const int SampleLimit = 120;
    public double MinimumScale { get; set; } = 1024;
    private Pen ReceivedPen = CreatePen("83E89A", 2);
    private Pen SentPen = CreatePen("69D8D3", 2);
    private Pen GridPen = CreatePen("31513B", 1);
    public NetworkChart()
    {
        Loaded += (_, _) => { ThemeService.Changed += OnThemeChanged; OnThemeChanged(null, EventArgs.Empty); };
        Unloaded += (_, _) => ThemeService.Changed -= OnThemeChanged;
    }
    private void OnThemeChanged(object? sender, EventArgs e)
    { ReceivedPen = CreatePen("83E89A", 2); SentPen = CreatePen("69D8D3", 2); GridPen = CreatePen("31513B", 1); InvalidateVisual(); }
    private static Pen CreatePen(string color, double width)
    { var brush = new SolidColorBrush(ThemeService.Color(color)); brush.Freeze(); var pen = new Pen(brush, width); pen.Freeze(); return pen; }

    public void AddSample(double received, double sent)
    {
        _received.Enqueue(Math.Max(0, received));
        _sent.Enqueue(Math.Max(0, sent));
        while (_received.Count > SampleLimit)
        {
            _received.Dequeue();
            _sent.Dequeue();
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 1 || height <= 1) return;
        for (var row = 0; row < 5; row++) context.DrawLine(GridPen, new Point(0, row * height / 4), new Point(width, row * height / 4));
        for (var column = 0; column < 7; column++) context.DrawLine(GridPen, new Point(column * width / 6, 0), new Point(column * width / 6, height));
        var max = Math.Max(MinimumScale, Math.Max(_received.DefaultIfEmpty().Max(), _sent.DefaultIfEmpty().Max()) * 1.15);
        DrawSeries(context, _received, ReceivedPen, width, height, max);
        DrawSeries(context, _sent, SentPen, width, height, max);
    }

    private static void DrawSeries(DrawingContext context, IEnumerable<double> values, Pen pen, double width, double height, double max)
    {
        var samples = values.ToArray();
        if (samples.Length < 2) return;
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            for (var index = 0; index < samples.Length; index++)
            {
                var point = new Point(index * width / (SampleLimit - 1), height - samples[index] / max * (height - 8) - 4);
                if (index == 0) path.BeginFigure(point, false, false);
                else path.LineTo(point, true, false);
            }
        }
        geometry.Freeze();
        context.DrawGeometry(null, pen, geometry);
    }
}
