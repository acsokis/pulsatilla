using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Pulsatilla.Wpf;

public enum RainQuality { Eco, Balanced, Off }

/// <summary>
/// Retained, tiled rain layers. WPF animates only three/four transforms; the application
/// has no frame callback, and neither text nor viewport-sized textures are rendered per frame.
/// </summary>
public sealed class MatrixRain : FrameworkElement
{
    private const int SpriteWidth = 18, MaxSpriteHeight = 240, VariantCount = 16;
    private static readonly Lazy<SpritePalette> Sprites = new(CreateSprites);
    private static readonly Lazy<SpritePalette> LightSprites = new(() => CreateSprites(true));
    private bool _lightTheme;
    private SpritePalette CurrentPalette => _lightTheme ? LightSprites.Value : Sprites.Value;
    private readonly ContainerVisual _scene = new();
    private readonly List<RainLayer> _layers = [];
    private readonly DispatcherTimer _alertTimer;
    private Window? _owner;
    private ClockGroup? _motionClock;
    private TimeSpan _elapsed;
    private double _period = MaxSpriteHeight, _speedRatio = .85;
    private int _frameRate;
    private bool _red;
    private RainQuality _quality = RainQuality.Eco;

    public MatrixRain()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        AddVisualChild(_scene);
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.LowQuality);
        // One-shot alert expiry only. Motion is entirely managed by WPF animation clocks.
        _alertTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(900)
        };
        _alertTimer.Tick += (_, _) => { _alertTimer.Stop(); SetAlertColor(false); };
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        IsVisibleChanged += (_, _) => UpdateMotion();
        RebuildScene();
    }

    public RainQuality Quality
    {
        get => _quality;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (_quality == value) return;
            _quality = value;
            RebuildScene();
            UpdateMotion();
        }
    }

    internal int ColumnCount => _layers.Sum(layer => layer.Columns.Count);
    internal int AnimationLayerCount => _layers.Count;
    internal bool IsAnimating => _motionClock is not null;
    internal ClockGroup? MotionClock => _motionClock;
    internal bool IsAlertActive => _red;
    internal int TargetFrameRate => _frameRate;
    internal long SpriteCacheBytes => CurrentPalette.Green.Concat(CurrentPalette.Red)
        .Sum(bitmap => (long)bitmap.PixelWidth * bitmap.PixelHeight * 4);
    internal double FirstDropPhase => _layers.Count == 0 ? 0 :
        Fraction(_layers[0].Columns[0].Phase + _layers[0].Position.Y / _period);

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => index == 0 ? _scene :
        throw new ArgumentOutOfRangeException(nameof(index));

    public void SetTrafficRate(double bytesPerSecond)
    {
        var intensity = double.IsFinite(bytesPerSecond)
            ? Math.Log10(1 + Math.Clamp(bytesPerSecond, 0, 20_000_000)) / Math.Log10(20_000_001) : 0;
        // Quantization avoids clock changes for inconsequential bandwidth fluctuations.
        var speed = Math.Round((.85 + .8 * intensity) * 20) / 20;
        if (speed == _speedRatio) return;
        _speedRatio = speed;
        if (_motionClock is not null) _motionClock.Controller!.SpeedRatio = speed;
    }

    public void FlashAlert()
    {
        if (!CanAnimate) return;
        SetAlertColor(true);
        _alertTimer.Stop();
        _alertTimer.Start();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        // Width changes only rearrange cached images. Height changes retain clock time,
        // so maximization changes the travel distance without restarting any stream.
        if (sizeInfo.HeightChanged) StopMotion();
        _period = Math.Max(0, ActualHeight) + MaxSpriteHeight;
        PaintScene();
        if (_motionClock is null) PositionStaticLayers();
        UpdateMotion();
    }

    private bool CanAnimate => IsLoaded && IsVisible && ActualWidth > 0 && ActualHeight > 0 &&
        _quality != RainQuality.Off && SystemParameters.ClientAreaAnimation &&
        _owner?.WindowState != WindowState.Minimized;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ThemeService.Changed += OnThemeChanged;
        OnThemeChanged(null, EventArgs.Empty);
        _owner = Window.GetWindow(this);
        if (_owner is not null)
        {
            _owner.StateChanged += OnOwnerStateChanged;
            _owner.Activated += OnOwnerStateChanged;
            _owner.Deactivated += OnOwnerStateChanged;
        }
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        RenderCapability.TierChanged += OnOwnerStateChanged;
        UpdateMotion();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ThemeService.Changed -= OnThemeChanged;
        StopMotion();
        ClearAlert();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        RenderCapability.TierChanged -= OnOwnerStateChanged;
        if (_owner is not null)
        {
            _owner.StateChanged -= OnOwnerStateChanged;
            _owner.Activated -= OnOwnerStateChanged;
            _owner.Deactivated -= OnOwnerStateChanged;
            _owner = null;
        }
    }

    private void OnOwnerStateChanged(object? sender, EventArgs e) => UpdateMotion();
    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_lightTheme == ThemeService.IsLight) return;
        _lightTheme = ThemeService.IsLight;
        PaintScene();
    }
    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation)) UpdateMotion();
    }

    private void UpdateMotion()
    {
        if (!CanAnimate)
        {
            StopMotion();
            ClearAlert();
            return;
        }
        var frameRate = _owner is { IsActive: false } ? 5 : _quality == RainQuality.Eco ? 15 : 24;
        if ((RenderCapability.Tier >> 16) == 0) frameRate = Math.Min(frameRate, 10);
        if (_motionClock is not null && _frameRate == frameRate) return;
        StopMotion();
        _frameRate = frameRate;
        var timeline = new ParallelTimeline { Duration = Duration.Forever };
        // DesiredFrameRate is a WPF timing guideline, rather than a guaranteed display fps.
        Timeline.SetDesiredFrameRate(timeline, frameRate);
        foreach (var layer in _layers)
            timeline.Children.Add(new DoubleAnimation(0, _period, TimeSpan.FromSeconds(layer.CycleSeconds))
            {
                RepeatBehavior = RepeatBehavior.Forever
            });
        timeline.Freeze();
        _motionClock = (ClockGroup)timeline.CreateClock(true);
        for (var index = 0; index < _layers.Count; index++)
            _layers[index].Position.ApplyAnimationClock(TranslateTransform.YProperty,
                (AnimationClock)_motionClock.Children[index]);
        _motionClock.Controller!.SpeedRatio = _speedRatio;
        _motionClock.Controller.SeekAlignedToLastTick(_elapsed, TimeSeekOrigin.BeginTime);
    }

    private void StopMotion()
    {
        if (_motionClock is not null)
        {
            _elapsed = _motionClock.CurrentTime ?? _elapsed;
            foreach (var layer in _layers) layer.Position.ApplyAnimationClock(TranslateTransform.YProperty, null);
            _motionClock.Controller!.Remove();
            _motionClock = null;
            PositionStaticLayers();
        }
        _frameRate = 0;
    }

    private void PositionStaticLayers()
    {
        foreach (var layer in _layers)
            layer.Position.Y = Fraction(_elapsed.TotalSeconds / layer.CycleSeconds) * _period;
    }

    private void RebuildScene()
    {
        StopMotion();
        _alertTimer.Stop();
        _red = false;
        _elapsed = TimeSpan.Zero;
        _scene.Children.Clear();
        _layers.Clear();
        if (_quality == RainQuality.Off) return;
        var count = _quality == RainQuality.Eco ? 32 : 48;
        var layerCount = _quality == RainQuality.Eco ? 3 : 4;
        for (var index = 0; index < layerCount; index++)
        {
            var cycleSeconds = index switch { 0 => 22d, 1 => 17d, 2 => 13d, _ => 10d };
            var layer = new RainLayer(cycleSeconds, .55 + .15 * index);
            _layers.Add(layer);
            _scene.Children.Add(layer.Visual);
        }
        var random = new Random(1729);
        for (var index = 0; index < count; index++)
            _layers[index % layerCount].Columns.Add(new RainColumn(
                (index + .3 + .4 * random.NextDouble()) / count,
                random.NextDouble(), random.Next(VariantCount)));
        PaintScene();
    }

    private void PaintScene()
    {
        if (_layers.Count == 0 || ActualWidth <= 0 || ActualHeight <= 0) return;
        var palette = _red ? CurrentPalette.Red : CurrentPalette.Green;
        foreach (var layer in _layers)
        {
            using var context = layer.Visual.RenderOpen();
            foreach (var column in layer.Columns)
            {
                var sprite = palette[column.Variant];
                var x = Math.Round(column.X * Math.Max(0, ActualWidth - SpriteWidth));
                var y = column.Phase * _period - MaxSpriteHeight;
                // Adjacent copies form a seamless vertical tile. Their textures remain
                // small; never cache a bitmap of a layer or of the entire window.
                context.DrawImage(sprite, new Rect(x, y, SpriteWidth, sprite.PixelHeight));
                context.DrawImage(sprite, new Rect(x, y - _period, SpriteWidth, sprite.PixelHeight));
            }
        }
    }

    private void ClearAlert()
    {
        _alertTimer.Stop();
        SetAlertColor(false);
    }

    private void SetAlertColor(bool red)
    {
        if (_red == red) return;
        _red = red;
        PaintScene();
    }

    private static double Fraction(double value) => value - Math.Floor(value);

    private static SpritePalette CreateSprites() => CreateSprites(false);
    private static SpritePalette CreateSprites(bool lightTheme)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZアイウエオカキクケコサシスセソタチツテト";
        var random = new Random(31337);
        var typeface = new Typeface("Consolas");
        var green = new BitmapSource[VariantCount];
        var red = new BitmapSource[VariantCount];
        for (var variant = 0; variant < VariantCount; variant++)
        {
            var rows = 9 + variant % 4 * 2;
            var letters = Enumerable.Range(0, rows).Select(_ => alphabet[random.Next(alphabet.Length)]).ToArray();
            for (var color = 0; color < 2; color++)
            {
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                    for (var row = 0; row < rows; row++)
                    {
                        var alpha = (byte)(12 + 215 * Math.Pow((row + 1d) / rows, 1.7));
                        var brush = new SolidColorBrush(row == rows - 1
                            ? color == 0 ? lightTheme ? Color.FromRgb(24, 75, 42) : Color.FromRgb(218, 255, 229)
                                : lightTheme ? Color.FromRgb(135, 22, 40) : Color.FromRgb(255, 225, 228)
                            : color == 0 ? lightTheme ? Color.FromArgb(alpha, 34, (byte)(112 + variant * 3), 65) : Color.FromArgb(alpha, 62, (byte)(200 + variant * 3), 105)
                                : lightTheme ? Color.FromArgb(alpha, 174, 36, 65) : Color.FromArgb(alpha, 255, 65, 85));
                        brush.Freeze();
                        context.DrawText(new FormattedText(letters[row].ToString(), CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, 13, brush, 1), new Point(1, row * 16));
                    }
                var bitmap = new RenderTargetBitmap(SpriteWidth, rows * 16, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(visual);
                bitmap.Freeze();
                (color == 0 ? green : red)[variant] = bitmap;
            }
        }
        return new SpritePalette(green, red);
    }

    private sealed record SpritePalette(BitmapSource[] Green, BitmapSource[] Red);
    private sealed record RainColumn(double X, double Phase, int Variant);
    private sealed class RainLayer
    {
        public DrawingVisual Visual { get; } = new();
        public TranslateTransform Position { get; } = new();
        public List<RainColumn> Columns { get; } = [];
        public double CycleSeconds { get; }
        public RainLayer(double cycleSeconds, double opacity)
        {
            CycleSeconds = cycleSeconds;
            Visual.Transform = Position;
            Visual.Opacity = opacity;
        }
    }
}
