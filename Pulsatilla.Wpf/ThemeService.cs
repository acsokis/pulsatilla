using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Pulsatilla.Wpf;

public enum ThemeMode { Dark, Light, System }

public static class ThemeService
{
    private static readonly Dictionary<string, string> LightColors = LoadPalette();
    private static bool _initialized;
    public static ThemeMode Mode { get; private set; } = ThemeMode.Dark;
    public static bool IsLight { get; private set; }
    public static event EventHandler? Changed;

    public static void Initialize()
    {
        if (_initialized) return;
        SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
        _initialized = true;
    }
    public static void Shutdown()
    {
        if (!_initialized) return;
        SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
        _initialized = false;
    }
    public static void Apply(ThemeMode mode)
    {
        if (!Enum.IsDefined(mode)) mode = ThemeMode.Dark;
        Mode = mode;
        IsLight = mode == ThemeMode.Light || mode == ThemeMode.System && WindowsUsesLightTheme();
        foreach (var (dark, light) in LightColors)
        {
            var color = (Color)ColorConverter.ConvertFromString("#" + (IsLight ? light : dark));
            Application.Current.Resources["ThemeColor_" + dark] = color;
            // Brushes declared inside a merged dictionary can resolve that dictionary's
            // original Color before the application override. Update the shared brush too.
            if (Application.Current.TryFindResource("Theme_" + dark) is SolidColorBrush brush && !brush.IsFrozen)
                brush.Color = color;
            else Application.Current.Resources["Theme_" + dark] = new SolidColorBrush(color);
        }
        foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
        Changed?.Invoke(null, EventArgs.Empty);
    }
    public static Color Color(string originalHex)
    {
        var hex = originalHex.TrimStart('#').ToUpperInvariant();
        return Application.Current.TryFindResource("ThemeColor_" + hex) is Color color ? color :
            (Color)ColorConverter.ConvertFromString("#" + hex);
    }
    public static SolidColorBrush Brush(string originalHex)
    {
        var hex = originalHex.TrimStart('#').ToUpperInvariant();
        return Application.Current.TryFindResource("Theme_" + hex) as SolidColorBrush ?? new SolidColorBrush(Color(hex));
    }
    public static void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero) return;
        var dark = IsLight ? 0 : 1;
        if (DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int)) < 0)
            DwmSetWindowAttribute(handle, 19, ref dark, sizeof(int));
    }
    private static void OnWindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (Mode != ThemeMode.System || Application.Current is not { } app || app.Dispatcher.HasShutdownStarted) return;
        app.Dispatcher.BeginInvoke(() => { if (Mode == ThemeMode.System) Apply(Mode); });
    }
    private static bool WindowsUsesLightTheme()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 0) is int value && value != 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { return false; }
    }
    private static Dictionary<string, string> LoadPalette()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Pulsatilla.Wpf.ThemePalette.json")!;
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
