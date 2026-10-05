using System.Globalization;
using System.IO;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulsatilla.Wpf;

/// <summary>Bounded MIME text extraction. Never renders HTML or opens attachments.</summary>
internal static class LocalEmailParser
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(150);
    private static readonly Regex EncodedWord = new(@"=\?(?<charset>[\w-]+)\?(?<kind>[bq])\?(?<data>[^?]*)\?=", RegexOptions.IgnoreCase, Timeout);
    static LocalEmailParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static EmailMessage Parse(string raw, bool byteTransport = false)
    {
        if (raw.Length > EmailSafetyService.MaximumMessageLength) throw new ArgumentException("Message is too large; maximum is 128,000 characters.");
        var warnings = new List<string>(); var attachments = new List<string>(); var bodies = new List<string>();
        raw = raw.TrimStart('\uFEFF').Replace("\r\n", "\n");
        var (headers, body) = Split(raw);
        if (headers.Count == 0) return new("", "", "", "", byteTransport ? Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(raw)) : raw);
        string Header(string name) => DecodeWords(headers.GetValueOrDefault(name, ""), warnings);
        var parts = 0;
        Extract(headers, body, 0);
        return new(Header("From"), Header("Reply-To"), string.Join(",", new[] { Header("To"), Header("Cc"), Header("Bcc") }.Where(value => value.Length > 0)),
            Header("Subject"), string.Join("\n\n", bodies)) { AttachmentNames = attachments, ImportWarnings = warnings.Distinct().ToArray() };

        void Extract(Dictionary<string, string> partHeaders, string payload, int depth)
        {
            if (++parts > 64 || depth > 6) { warnings.Add("MIME nesting/part limit reached; some content was not inspected."); return; }
            ContentType type;
            try { type = new ContentType(partHeaders.GetValueOrDefault("Content-Type", "text/plain; charset=utf-8")); }
            catch (FormatException) { warnings.Add("Invalid Content-Type; content could not be fully decoded."); bodies.Add(payload); return; }
            var name = type.Name;
            var attachment = false;
            if (partHeaders.TryGetValue("Content-Disposition", out var disposition))
            {
                try { var value = new ContentDisposition(disposition); attachment = value.DispositionType.Equals("attachment", StringComparison.OrdinalIgnoreCase); name = value.FileName ?? name; }
                catch (FormatException) { warnings.Add("Invalid attachment metadata; verify attachments independently."); attachment = true; }
            }
            if (!string.IsNullOrEmpty(name)) attachments.Add(DecodeWords(name, warnings));
            if (attachment) { warnings.Add("Attachment contents were not inspected; only available filenames were checked."); return; }
            if (type.MediaType.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase))
            {
                var boundary = type.Boundary;
                if (string.IsNullOrEmpty(boundary) || boundary.Length > 70) { warnings.Add("Invalid multipart boundary; content could not be decoded."); return; }
                var builder = new StringBuilder(); var inside = false; var closed = false;
                using var reader = new StringReader(payload);
                while (reader.ReadLine() is { } line)
                {
                    var marker = line.TrimEnd(' ', '\t');
                    if (marker == "--" + boundary || marker == "--" + boundary + "--")
                    {
                        if (inside) { var part = Split(builder.ToString()); Extract(part.Headers, part.Body, depth + 1); builder.Clear(); }
                        if (marker.EndsWith("--", StringComparison.Ordinal) && marker == "--" + boundary + "--") { closed = true; inside = false; break; }
                        inside = true;
                    }
                    else if (inside) builder.AppendLine(line);
                }
                if (!closed) { warnings.Add("Multipart message is incomplete; some content may be missing."); if (inside && builder.Length > 0) { var part = Split(builder.ToString()); Extract(part.Headers, part.Body, depth + 1); } }
                return;
            }
            if (!type.MediaType.Equals("text/plain", StringComparison.OrdinalIgnoreCase) && !type.MediaType.Equals("text/html", StringComparison.OrdinalIgnoreCase))
            { warnings.Add("A non-text or encrypted message part was not inspected."); return; }
            var transfer = partHeaders.GetValueOrDefault("Content-Transfer-Encoding", "").Trim().ToLowerInvariant();
            try
            {
                var bytes = transfer switch
                {
                    "base64" => Convert.FromBase64String(payload),
                    "quoted-printable" => QuotedPrintable(payload),
                    "" or "7bit" or "8bit" or "binary" => byteTransport ? Encoding.Latin1.GetBytes(payload) : null,
                    _ => throw new FormatException("Unsupported transfer encoding.")
                };
                bodies.Add(bytes is null ? payload : Decode(bytes, type.CharSet ?? "utf-8"));
            }
            catch (Exception ex) when (ex is FormatException or ArgumentException or DecoderFallbackException)
            { warnings.Add("A text part has invalid or unsupported encoding; review the original message in your mail app."); bodies.Add(payload); }
        }
    }

    private static (Dictionary<string, string> Headers, string Body) Split(string raw)
    {
        raw = raw.Replace("\r\n", "\n");
        var separator = raw.IndexOf("\n\n", StringComparison.Ordinal);
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (separator < 0) return (headers, raw);
        var unfolded = Regex.Replace(raw[..separator], "\n[ \t]+", " ", RegexOptions.None, Timeout);
        foreach (var line in unfolded.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) return (new(StringComparer.OrdinalIgnoreCase), raw);
            var key = line[..colon].Trim(); var value = line[(colon + 1)..].Trim();
            headers[key] = headers.TryGetValue(key, out var previous) ? previous + ", " + value : value;
        }
        return (headers, raw[(separator + 2)..]);
    }

    private static string DecodeWords(string value, List<string> warnings)
    {
        value = Regex.Replace(value, @"(?<=\?=)[ \t]+(?==\?)", "", RegexOptions.None, Timeout);
        return EncodedWord.Replace(value, match =>
        {
            try { return Decode(match.Groups["kind"].Value.Equals("b", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(match.Groups["data"].Value) : QuotedPrintable(match.Groups["data"].Value.Replace('_', ' ')), match.Groups["charset"].Value); }
            catch (Exception ex) when (ex is FormatException or ArgumentException or DecoderFallbackException)
            { warnings.Add("An encoded header could not be decoded."); return match.Value; }
        });
    }

    private static string Decode(byte[] bytes, string charset) => Encoding.GetEncoding(charset,
        EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback).GetString(bytes);

    private static byte[] QuotedPrintable(string value)
    {
        var bytes = new List<byte>(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var c = value[index];
            if (c != '=') { if (c > 255) throw new FormatException("Invalid quoted-printable character."); bytes.Add((byte)c); continue; }
            if (index + 1 < value.Length && value[index + 1] == '\n') { index++; continue; }
            if (index + 2 < value.Length && value[index + 1] == '\r' && value[index + 2] == '\n') { index += 2; continue; }
            if (index + 2 >= value.Length || !byte.TryParse(value.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var decoded))
                throw new FormatException("Invalid quoted-printable escape.");
            bytes.Add(decoded); index += 2;
        }
        return bytes.ToArray();
    }
}
