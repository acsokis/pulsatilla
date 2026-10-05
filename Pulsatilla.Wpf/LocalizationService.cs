using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace Pulsatilla.Wpf;

public sealed record LanguageOption(string Code, string NativeName);

public static class LocalizationService
{
    private const string DefaultCode = "en";
    private static readonly Assembly AppAssembly = Assembly.GetExecutingAssembly();
    private static readonly string SettingsPath = AppStorage.FilePath("settings.json");
    private static readonly IReadOnlyList<LanguageOption> Supported =
    [
        new("en", "English"), new("de", "Deutsch"), new("hu", "Magyar"),
        new("es", "Español"), new("fr", "Français"), new("pt-BR", "Português (Brasil)"),
        new("it", "Italiano"), new("ru", "Русский"), new("tr", "Türkçe"),
        new("zh-CN", "简体中文"), new("ja", "日本語"), new("ko", "한국어"),
        new("hi", "हिन्दी"), new("fa", "فارسی"), new("ur", "اردو"),
        new("ar", "العربية"), new("id", "Bahasa Indonesia"), new("pl", "Polski")
    ];

    private static readonly Dictionary<string, string> _english = LoadDictionary(DefaultCode);
    private static Dictionary<string, string> _current = _english;

    public static IReadOnlyList<LanguageOption> Languages => Supported;
    public static string CurrentCode { get; private set; } = DefaultCode;
    public static bool IsRightToLeft => CultureInfo.GetCultureInfo(CurrentCode).TextInfo.IsRightToLeft;

    public static string LoadSavedCode()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(SettingsPath));
                if (document.RootElement.TryGetProperty("culture", out var culture))
                {
                    var code = culture.GetString();
                    var language = Supported.FirstOrDefault(language => language.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (language is not null) return language.Code;
                }
            }
        }
        catch (Exception) { }
        var systemCode = CultureInfo.CurrentUICulture.Name;
        return Supported.FirstOrDefault(language => language.Code.Equals(systemCode, StringComparison.OrdinalIgnoreCase))?.Code
            ?? Supported.FirstOrDefault(language => language.Code.Equals(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase))?.Code
            ?? DefaultCode;
    }

    public static bool SetLanguage(string code, bool persist = true)
    {
        var language = Supported.FirstOrDefault(item => item.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
        if (language is null) return false;
        var dictionary = LoadDictionary(language.Code);
        if (language.Code != DefaultCode && dictionary.Count == 0) return false;

        CurrentCode = language.Code;
        _current = dictionary;
        try
        {
            var culture = CultureInfo.GetCultureInfo(language.Code);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        catch (CultureNotFoundException) { }

        try
        {
            if (persist)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                var settings = File.Exists(SettingsPath)
                    ? JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(SettingsPath)) ?? new()
                    : new Dictionary<string, JsonElement>();
                settings["culture"] = JsonSerializer.SerializeToElement(CurrentCode);
                File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings));
            }
        }
        catch (Exception) { }
        return true;
    }

    public static string Translate(string source) =>
        _current.TryGetValue(source, out var value) ? value : _english.GetValueOrDefault(source, source);

    public static string Format(FormattableString text) =>
        string.Format(CultureInfo.CurrentCulture, Translate(text.Format), text.GetArguments());

    public static string ResourceKey(string source) => "Loc_" +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)))[..12];

    // DataGrid columns have no resource inheritance context. Keep their translation source explicitly.
    public static readonly DependencyProperty SourceProperty = DependencyProperty.RegisterAttached(
        "Source", typeof(string), typeof(LocalizationService), new PropertyMetadata(null));
    public static void SetSource(DependencyObject element, string value) => element.SetValue(SourceProperty, value);
    public static string? GetSource(DependencyObject element) => element.GetValue(SourceProperty) as string;

    public static void Apply(Window window)
    {
        window.FlowDirection = IsRightToLeft ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        window.Language = System.Windows.Markup.XmlLanguage.GetLanguage(CurrentCode);
        foreach (var source in _english.Keys)
            Application.Current.Resources[ResourceKey(source)] = Translate(source);
        ApplyColumnHeaders(window);
    }

    private static void ApplyColumnHeaders(DependencyObject node)
    {
        if (node is DataGrid grid)
            foreach (var column in grid.Columns)
                if (GetSource(column) is string source) column.Header = Translate(source);
        foreach (var child in LogicalTreeHelper.GetChildren(node))
            if (child is DependencyObject dependencyChild) ApplyColumnHeaders(dependencyChild);
    }

    private static Dictionary<string, string> LoadDictionary(string code)
    {
        try
        {
            var name = $"Pulsatilla.Wpf.Resources.Locales.{code}.json";
            using var stream = AppAssembly.GetManifestResourceStream(name);
            return stream is null ? new Dictionary<string, string>() :
                JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? new Dictionary<string, string>();
        }
        catch (Exception) { return new Dictionary<string, string>(); }
    }
}
