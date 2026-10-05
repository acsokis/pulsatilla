using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace Pulsatilla.Wpf;

public sealed record CreatorProfile(string Name, string Location, string Instagram, string Linkedin, string Facebook, string Github);

public static class AboutContent
{
    public const string OriginalCreatorFacebook = "https://www.facebook.com/goldnerivan#";
    public static string Readme => ReadResource("Pulsatilla.Wpf.README.md");
    public static string LicenseText => ReadResource("Pulsatilla.Wpf.LICENSE");
    public static string Services => ReadResource("Pulsatilla.Wpf.Services.md");
    public static string Privacy => ReadResource("Pulsatilla.Wpf.Privacy.md");
    public static string Origin => ReadResource("Pulsatilla.Wpf.Origin.md");
    public static CreatorProfile Creator => JsonSerializer.Deserialize<CreatorProfile>(ReadResource("Pulsatilla.Wpf.CreatorProfile.json"),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static FlowDocument CreateReadmeDocument() => CreateDocument(Readme);
    public static FlowDocument CreateDocument(string markdown)
    {
        var document = new FlowDocument { FontFamily = new FontFamily("Segoe UI"), FontSize = 13,
            PagePadding = new Thickness(22), ColumnWidth = double.PositiveInfinity, FlowDirection = FlowDirection.LeftToRight };
        document.SetResourceReference(FlowDocument.ForegroundProperty, "Theme_E6F2E8");
        var lines = markdown.Replace("\r\n", "\n").Split('\n'); var code = false; var codeLines = new System.Text.StringBuilder();
        foreach (var line in lines)
        {
            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                if (code) { var block = new Paragraph(new Run(codeLines.ToString().TrimEnd())) { FontFamily = new FontFamily("Consolas"), FontSize = 11, Padding = new Thickness(12) };
                    block.SetResourceReference(Paragraph.BackgroundProperty, "Theme_07100B"); document.Blocks.Add(block); codeLines.Clear(); }
                code = !code; continue;
            }
            if (code) { codeLines.AppendLine(line); continue; }
            if (line.Length == 0) continue;
            var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
            var text = heading.Success ? heading.Groups[2].Value : line.StartsWith("- ", StringComparison.Ordinal) ? "• " + line[2..] : line;
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, line.StartsWith("- ", StringComparison.Ordinal) ? 5 : 10) };
            if (heading.Success) { paragraph.FontSize = heading.Groups[1].Length == 1 ? 26 : 19; paragraph.FontWeight = FontWeights.SemiBold; paragraph.Margin = new Thickness(0, 16, 0, 10); }
            AddInlines(paragraph, text); document.Blocks.Add(paragraph);
        }
        return document;
    }
    private static void AddInlines(Paragraph paragraph, string text)
    {
        var offset = 0;
        foreach (Match match in Regex.Matches(text, @"\[(?<label>[^\]]+)\]\((?<url>[^)]+)\)|\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`"))
        {
            paragraph.Inlines.Add(new Run(text[offset..match.Index]));
            if (match.Groups["url"].Success && Uri.TryCreate(match.Groups["url"].Value, UriKind.Absolute, out var uri) && uri.Scheme == "https")
            {
                var link = new Hyperlink(new Run(match.Groups["label"].Value)) { NavigateUri = uri };
                link.SetResourceReference(Hyperlink.ForegroundProperty, "Theme_75DE88");
                link.RequestNavigate += (_, e) => { OpenLink(e.Uri.AbsoluteUri); e.Handled = true; };
                paragraph.Inlines.Add(link);
            }
            else if (match.Groups["bold"].Success) paragraph.Inlines.Add(new Bold(new Run(match.Groups["bold"].Value)));
            else if (match.Groups["code"].Success) paragraph.Inlines.Add(new Run(match.Groups["code"].Value) { FontFamily = new FontFamily("Consolas"), FontSize = 12 });
            else paragraph.Inlines.Add(new Run(match.Groups["label"].Value + " (" + match.Groups["url"].Value + ")"));
            offset = match.Index + match.Length;
        }
        paragraph.Inlines.Add(new Run(text[offset..]));
    }
    public static void OpenLink(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https") return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        { MessageBox.Show("Could not open the link: " + ex.Message, "Pulsatilla", MessageBoxButton.OK, MessageBoxImage.Information); }
    }
    private static string ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name) ?? throw new InvalidOperationException("Missing embedded content: " + name);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
}
