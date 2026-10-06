namespace Pulsatilla.Wpf;

[Flags]
public enum HexPatternContext { Packet = 1, Scanner = 2, File = 4, EmailAttachment = 8, All = 15 }
public sealed record HexPatternMatch(string Id, string Name, string Category, int Offset, int Length, string Severity, string Description);
public sealed record HexAnalysisResult(int BytesInspected, bool Truncated, IReadOnlyList<HexPatternMatch> Matches)
{
    public string Interpretation => "Signatures identify byte patterns only. A match does not confirm an attack; no match does not establish safety.";
}

/// <summary>Immutable, bounded byte rule. FF mask compares a byte; 00 ignores it.</summary>
public sealed class HexPatternRule
{
    internal readonly byte[] Bytes;
    internal readonly byte[] Mask;
    public string Id { get; }
    public string Name { get; }
    public string Category { get; }
    public int? Offset { get; }
    public string Severity { get; }
    public string Description { get; }
    public HexPatternContext Context { get; }
    public HexPatternRule(string id, string name, string category, byte[] bytes, byte[]? mask = null,
        int? offset = null, string severity = "Information", string description = "", HexPatternContext context = HexPatternContext.All)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length is < 1 or > 128 || mask is not null && mask.Length != bytes.Length || offset < 0 || offset > HexPatternEngine.MaximumSampleBytes)
            throw new ArgumentException("Invalid bounded pattern length, mask or offset.");
        if (mask is not null && (mask.Any(b => b is not 0 and not 255) || mask.All(b => b == 0)))
            throw new ArgumentException("A mask must contain a compared byte and only 00/FF values.");
        if (new[] { id, name, category, severity, description }.Any(s => s is null || s.Length > 1024) || string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Invalid pattern metadata.");
        Id = id; Name = name; Category = category; Offset = offset; Severity = severity; Description = description; Context = context;
        Bytes = (byte[])bytes.Clone(); Mask = mask is null ? Enumerable.Repeat((byte)255, bytes.Length).ToArray() : (byte[])mask.Clone();
    }
}

public sealed class HexPatternEngine
{
    public const int MaximumSampleBytes = 65_536;
    public const int MaximumMatches = 256;
    private const int MaximumComparisons = 8_000_000;
    private readonly HexPatternRule[] _rules;
    public HexPatternEngine(IEnumerable<HexPatternRule> rules)
    {
        _rules = rules.Take(257).ToArray();
        if (_rules.Length > 256 || _rules.Any(r => r is null) || _rules.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != _rules.Length)
            throw new ArgumentException("Maximum 256 unique rules.");
    }
    public HexAnalysisResult Analyze(ReadOnlySpan<byte> sample, HexPatternContext context = HexPatternContext.All, CancellationToken cancellationToken = default)
    {
        var truncated = sample.Length > MaximumSampleBytes;
        sample = sample[..Math.Min(sample.Length, MaximumSampleBytes)];
        var hits = new List<HexPatternMatch>(); var comparisons = 0;
        foreach (var rule in _rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if ((rule.Context & context) == 0 || rule.Bytes.Length > sample.Length) continue;
            var end = rule.Offset ?? sample.Length - rule.Bytes.Length;
            for (var index = rule.Offset ?? 0; index <= end && index <= sample.Length - rule.Bytes.Length; index++)
            {
                if ((index & 255) == 0) cancellationToken.ThrowIfCancellationRequested();
                var found = true;
                for (var b = 0; b < rule.Bytes.Length; b++)
                {
                    if (++comparisons > MaximumComparisons) return new(sample.Length, true, hits.ToArray());
                    if (rule.Mask[b] != 0 && rule.Bytes[b] != sample[index + b]) { found = false; break; }
                }
                if (!found) continue;
                hits.Add(new(rule.Id, rule.Name, rule.Category, index, rule.Bytes.Length, rule.Severity, rule.Description));
                if (hits.Count >= MaximumMatches) return new(sample.Length, true, hits.ToArray());
            }
        }
        return new(sample.Length, truncated, hits.ToArray());
    }
    private static HexPatternRule Magic(string id, string name, string hex) => new(id, name, "File signature", Convert.FromHexString(hex), offset: 0,
        description: "Format prefix only; content and authenticity are unverified.", context: HexPatternContext.File | HexPatternContext.EmailAttachment | HexPatternContext.Scanner);
    public static HexPatternEngine Default { get; } = new(new[] {
        Magic("pe", "DOS/PE executable candidate", "4D5A"), Magic("pdf", "PDF", "255044462D"), Magic("zip", "ZIP/container", "504B0304"),
        Magic("png", "PNG", "89504E470D0A1A0A"), Magic("jpeg", "JPEG", "FFD8FF"), Magic("gif", "GIF", "47494638"),
        Magic("ole", "OLE compound document", "D0CF11E0A1B11AE1"), Magic("elf", "ELF executable", "7F454C46"),
        new HexPatternRule("http-get", "HTTP request marker", "Protocol signature", System.Text.Encoding.ASCII.GetBytes("GET "), description: "May be ordinary HTTP or arbitrary text.", context: HexPatternContext.Packet | HexPatternContext.Scanner),
        new HexPatternRule("http-response", "HTTP response marker", "Protocol signature", System.Text.Encoding.ASCII.GetBytes("HTTP/1."), context: HexPatternContext.Packet | HexPatternContext.Scanner),
        new HexPatternRule("ssh", "SSH banner marker", "Protocol signature", System.Text.Encoding.ASCII.GetBytes("SSH-"), context: HexPatternContext.Packet | HexPatternContext.Scanner),
        new HexPatternRule("script", "Script tag", "Review indicator", System.Text.Encoding.ASCII.GetBytes("<script"), severity: "Review", description: "Can be legitimate HTML; never rendered or executed.", context: HexPatternContext.File | HexPatternContext.EmailAttachment | HexPatternContext.Scanner)
    });
}
