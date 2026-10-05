using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using static Pulsatilla.Wpf.EmailAddressRules;

namespace Pulsatilla.Wpf;

public sealed record EmailMessage(string From, string ReplyTo, string Recipients, string Subject, string Body)
{
    public IReadOnlyList<string> AttachmentNames { get; init; } = [];
    public IReadOnlyList<string> ImportWarnings { get; init; } = [];
}
public sealed record EmailSafetyResult(string Summary, IReadOnlyList<string> Findings, bool FilteredOut, int Score);

/// <summary>Offline review of pasted/imported messages. Never opens links, downloads files or reads a mailbox.</summary>
public static class EmailSafetyService
{
    public const int MaximumMessageLength = 128_000;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(150);
    private static readonly Regex Links = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, Timeout);
    private static readonly Regex Anchor = new(@"<a\b[^>]*href\s*=\s*[""'](?<url>[^""']+)[""'][^>]*>(?<label>[\s\S]*?)</a>", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex Urgency = new(@"\b(urgent|immediately|suspended|final warning|verify now|dringend|sofort|sürgős|azonnal)\b", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex Secrets = new(@"\b(password|passcode|verification code|one.time code|seed phrase|recovery phrase|jelsz(?:ó|av)\w*|ellenőrző kód\w*|passwort)\b", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex Requests = new(@"\b(send|reply|share|provide|enter|confirm|küld\w*|add meg|adja meg|válasz\w*|senden|eingeben)\b", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex Money = new(@"\b(gift card|wire transfer|bank details|crypto|bitcoin|ajándékkártya|átutalás|geschenkkarte)\b", RegexOptions.IgnoreCase, Timeout);
    private static readonly Regex RemoteAccess = new(@"\b(anydesk|teamviewer|remote access|távoli hozzáférés|fernzugriff)\b", RegexOptions.IgnoreCase, Timeout);

    public static EmailMessage ParseMessage(string raw) => LocalEmailParser.Parse(raw);
    public static EmailMessage ParseMessage(byte[] raw) => LocalEmailParser.Parse(Encoding.Latin1.GetString(raw), byteTransport: true);

    public static EmailSafetyResult Analyze(EmailMessage message, string watchedEmails = "", string trustedSenders = "", string blockedSenders = "")
    {
        if (message.Body.Length + message.Subject.Length > MaximumMessageLength)
            return new("Message too large to analyze.", ["Limit: 128,000 characters. No message content was sent anywhere."], true, 0);
        var watched = Tokens(watchedEmails).Select(Address).ToArray();
        if (watched.Any(value => value is null))
            return new("Invalid watched-email filter.", ["Use complete email addresses, separated by commas, semicolons or new lines."], true, 0);
        if (watched.Length > 0 && !Addresses(message.Recipients).Any(address => watched.Contains(address, StringComparer.OrdinalIgnoreCase)))
            return new("Skipped: recipient does not match your email filter.", ["Add the recipient explicitly, or clear the filter to analyze all messages."], true, 0);

        var findings = new List<string>();
        var score = 0;
        void Flag(int points, string reason) { if (!findings.Contains(reason)) { score += points; findings.Add(reason); } }
        var from = Address(message.From);
        var reply = Address(message.ReplyTo);
        if (from is null) Flag(10, "Sender is missing or invalid; verify the actual sender before acting.");
        if (MatchesSender(from, blockedSenders)) Flag(70, "Sender matches your blacklist. Treat the message as unwanted.");
        if (MatchesSender(from, trustedSenders)) findings.Add("Sender matches your whitelist. This does not authenticate the sender or override suspicious content.");
        if (message.ReplyTo.Length > 0 && reply is null) Flag(15, "Reply-To address is invalid.");
        else if (from is not null && reply is not null && Domain(from) != Domain(reply))
            Flag(20, "Reply-To uses a different domain from the sender. This can be legitimate, but confirm independently before replying.");

        foreach (var warning in message.ImportWarnings)
            if (warning.StartsWith("Attachment contents", StringComparison.Ordinal)) findings.Add(warning);
            else Flag(20, warning);
        var text = WebUtility.HtmlDecode(message.Subject + "\n" + message.Body + "\n" + string.Join("\n", message.AttachmentNames));
        var readable = Regex.Replace(text, @"<(?:br|/?(?:p|div|li|tr|h[1-6]))\b[^>]*>", " ", RegexOptions.IgnoreCase, Timeout);
        readable = Regex.Replace(readable, "<[^>]*>", "", RegexOptions.None, Timeout).Normalize(NormalizationForm.FormKC);
        readable = Regex.Replace(readable, "[\u200B-\u200D\u2060\uFEFF]", "", RegexOptions.None, Timeout);
        var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Links.Matches(text).Take(100))
        {
            if (!Uri.TryCreate(match.Value.TrimEnd('.', ',', ')', ']', ';'), UriKind.Absolute, out var uri)) continue;
            hosts.Add(uri.IdnHost);
            if (uri.Scheme == "http") Flag(10, "A link uses unencrypted HTTP. Do not submit credentials through it.");
            if (IPAddress.TryParse(uri.Host, out _)) Flag(20, "A link uses an IP address instead of a recognizable domain.");
            if (uri.UserInfo.Length > 0) Flag(25, "A link contains text before @ in its authority. That text is not the destination domain.");
            if (uri.IdnHost.Contains("xn--", StringComparison.OrdinalIgnoreCase))
                Flag(15, "An internationalized domain is present. Compare its spelling carefully; similar-looking characters can hide impersonation.");
            if (uri.IdnHost is "bit.ly" or "tinyurl.com" or "t.co" or "is.gd" or "cutt.ly")
                Flag(10, "A shortened link hides the final destination. It was not followed.");
        }
        foreach (Match match in Anchor.Matches(text).Take(100))
        {
            var destination = match.Groups["url"].Value.Trim();
            if (destination.StartsWith("//", StringComparison.Ordinal)) destination = "https:" + destination;
            var labelText = Regex.Replace(match.Groups["label"].Value, "<[^>]*>", "", RegexOptions.None, Timeout).Trim();
            if (Uri.TryCreate(destination, UriKind.Absolute, out var target))
            {
                if (target.Scheme is "javascript" or "data" or "file")
                    Flag(30, "A link requests executable content or a local file. Do not activate it.");
                if (Uri.TryCreate(labelText, UriKind.Absolute, out var label) &&
                    !target.IdnHost.Equals(label.IdnHost, StringComparison.OrdinalIgnoreCase))
                    Flag(30, "A displayed link points to a different domain than its label.");
            }
        }
        if (Secrets.IsMatch(readable) && Requests.IsMatch(readable))
            Flag(35, "Message asks for a password, recovery phrase or verification code. Do not share these by email or chat.");
        if (Urgency.IsMatch(readable)) Flag(10, "Urgent or threatening language pressures you to act quickly.");
        if (Money.IsMatch(readable) && (Urgency.IsMatch(readable) || Requests.IsMatch(readable)))
            Flag(25, "Payment, gift-card or cryptocurrency request combined with pressure or instructions. Verify through a known contact channel.");
        if (RemoteAccess.IsMatch(readable) && Requests.IsMatch(readable))
            Flag(25, "Message requests remote-access software. Verify the person independently before granting access.");
        if (Regex.IsMatch(readable, @"\b[^\s<>""']+\.(exe|scr|js|vbs|iso|img|lnk|bat|cmd)\b", RegexOptions.IgnoreCase, Timeout))
            Flag(25, "Executable or script attachment/link name detected. Do not run it without independent verification.");
        if (Regex.IsMatch(readable, @"\b(approve|accept|jóváhagy\w*|bestätig\w*)\b[\s\S]{0,80}\b(sign.in|login|notification|belép\w*|bejelentkez\w*|anmeldung)\b", RegexOptions.IgnoreCase, Timeout))
            Flag(30, "Message asks you to approve a sign-in notification. Reject requests you did not initiate and contact the service independently.");
        if (Regex.IsMatch(readable, @"\b(new bank account|changed bank details|új bankszámla|neue bankverbindung)\b", RegexOptions.IgnoreCase, Timeout) && Requests.IsMatch(readable))
            Flag(30, "A request changes payment-account details. Confirm with a known contact before transferring money.");
        if (hosts.Count > 0) findings.Add("Observed destination domains (links were not opened): " + string.Join(", ", hosts.Take(12)));
        if (score == 0) findings.Add("No configured warning pattern matched. This is not proof that the message is safe.");
        findings.Add("For login or payment requests, open the official app/site yourself. Confirm unusual requests using a previously known phone number; never share MFA codes.");
        findings.Add("Local analysis only. Mail authentication, encrypted attachments, mailbox filtering and breach/dark-web lookup were not performed.");
        return new(score >= 50 ? "High risk: verify independently before acting." : score >= 20
            ? "Review recommended: suspicious indicators found." : "Low observed risk; authenticity is unverified.", findings, false, Math.Min(score, 100));
    }

}
