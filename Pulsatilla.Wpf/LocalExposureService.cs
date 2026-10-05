using System.Text.Json;
using System.IO;
using Microsoft.VisualBasic.FileIO;

namespace Pulsatilla.Wpf;

public sealed record ExposureMatch(string Email, string Service, string ReportedDate);
public sealed record ExposureReview(int RowsReviewed, IReadOnlyList<ExposureMatch> Matches, int OmittedMatches);

/// <summary>Checks an explicitly supplied local CSV/JSON report; never searches the web.</summary>
public static class LocalExposureService
{
    public const int MaximumReportLength = 1_048_576;
    public static ExposureReview Review(string report, string format, string watchedEmails)
    {
        if (report.Length > MaximumReportLength) throw new ArgumentException("Report is too large; maximum is 1 MiB of text.");
        var addresses = EmailAddressRules.Tokens(watchedEmails).Select(EmailAddressRules.Address).ToArray();
        if (addresses.Length == 0 || addresses.Any(address => address is null)) throw new ArgumentException("Enter complete watched email addresses before importing a report.");
        var watched = addresses.OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var matches = new List<ExposureMatch>(); var distinct = new HashSet<ExposureMatch>(); var rows = 0; var omitted = 0;
        void Row(string email, string service, string date)
        {
            if (++rows > 10_000) throw new ArgumentException("Report contains more than 10,000 records.");
            var address = EmailAddressRules.Address(email);
            if (address is null || !watched.Contains(address)) return;
            var match = new ExposureMatch(address, Clean(service), Clean(date));
            if (!distinct.Add(match)) return;
            if (matches.Count < 100) matches.Add(match); else omitted++;
        }
        if (format.Equals(".json", StringComparison.OrdinalIgnoreCase))
        {
            using var document = JsonDocument.Parse(report, new JsonDocumentOptions { MaxDepth = 8 });
            if (document.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("JSON report must be an array of objects with email, service and date fields.");
            foreach (var item in document.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("email", out var email) || email.ValueKind != JsonValueKind.String)
                    throw new ArgumentException("Each JSON record must contain an email string.");
                string Field(string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
                Row(email.GetString()!, Field("service"), Field("date"));
            }
        }
        else if (format.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StringReader(report.TrimStart('\uFEFF'));
            using var parser = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true };
            parser.SetDelimiters(",");
            var headers = parser.ReadFields() ?? [];
            var emailIndex = Array.FindIndex(headers, value => value.Equals("email", StringComparison.OrdinalIgnoreCase));
            var serviceIndex = Array.FindIndex(headers, value => value.Equals("service", StringComparison.OrdinalIgnoreCase));
            var dateIndex = Array.FindIndex(headers, value => value.Equals("date", StringComparison.OrdinalIgnoreCase));
            if (emailIndex < 0) throw new ArgumentException("CSV report requires an email column; service and date are optional.");
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields() ?? [];
                if (emailIndex >= fields.Length) throw new ArgumentException("A CSV row is missing the email column.");
                string Field(int index) => index >= 0 && index < fields.Length ? fields[index] : "";
                Row(fields[emailIndex], Field(serviceIndex), Field(dateIndex));
            }
        }
        else throw new ArgumentException("Choose a CSV or JSON exposure report.");
        return new(rows, matches, omitted);
    }
    private static string Clean(string value) => new(value.Where(c => !char.IsControl(c)).Take(200).ToArray());
}
