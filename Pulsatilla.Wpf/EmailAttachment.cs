using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Pulsatilla.Wpf;

/// <summary>Decoded local memory only; no extraction, execution or link following.</summary>
public sealed class EmailAttachment
{
    public const int MaximumAggregateBytes = 128_000;
    private readonly byte[] _bytes;
    public string FileName { get; }
    public string MimeType { get; }
    public int Size => _bytes.Length;
    public string Extension => System.IO.Path.GetExtension(FileName);
    public string Sha256 { get; }
    public double Entropy { get; }
    public HexAnalysisResult Analysis { get; }
    public IReadOnlyList<string> EmbeddedUrls { get; }
    public string Signature => "Unknown: in-memory attachment was not written to disk for Authenticode verification.";
    public EmailAttachment(string fileName, string mimeType, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumAggregateBytes) throw new ArgumentException("Attachment exceeds local bound.");
        FileName = fileName[..Math.Min(fileName.Length, 1024)]; MimeType = mimeType[..Math.Min(mimeType.Length, 256)]; _bytes = bytes.ToArray();
        Sha256 = Convert.ToHexString(SHA256.HashData(_bytes)); Analysis = HexPatternEngine.Default.Analyze(_bytes, HexPatternContext.EmailAttachment);
        var counts = new int[256]; foreach (var b in _bytes) counts[b]++;
        Entropy = _bytes.Length == 0 ? 0 : counts.Where(c => c > 0).Sum(c => { var p = (double)c / _bytes.Length; return -p * Math.Log2(p); });
        try { EmbeddedUrls = Regex.Matches(Encoding.Latin1.GetString(_bytes.AsSpan(0, Math.Min(_bytes.Length, 65_536))), @"https?://[^\s<>""']{1,512}", RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100)).Select(m => m.Value).Distinct(StringComparer.Ordinal).Take(20).ToArray(); }
        catch (RegexMatchTimeoutException) { EmbeddedUrls = []; }
    }
    public byte[] CopyPreview() => _bytes.AsSpan(0, Math.Min(_bytes.Length, HexPatternEngine.MaximumSampleBytes)).ToArray();
}
