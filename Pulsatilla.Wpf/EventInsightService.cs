using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulsatilla.Wpf;

public sealed class EventInsightService
{
    private const string ModelName = "qwen2.5:3b";
    private static readonly HttpClient LocalClient = new() { Timeout = TimeSpan.FromSeconds(12) };

    public async Task<EventInsight> ExplainAsync(string eventText, IReadOnlyList<string> recentEvents,
        CancellationToken cancellationToken = default)
    {
        var context = string.Join(Environment.NewLine, recentEvents.Take(8).Select(line => line[..Math.Min(line.Length, 400)]));
        var prompt = $"You are a cautious Windows network diagnostics assistant. Explain likely causes and safe checks. " +
            "Do not claim an attack is confirmed from one event. Do not recommend disabling security protections. " +
            "Answer in Hungarian, in 2-4 short sentences.\nSelected event: {eventText}\nRecent context:\n{context}";
        try
        {
            using var response = await LocalClient.PostAsJsonAsync("http://127.0.0.1:11434/api/generate",
                new { model = ModelName, prompt, stream = false }, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var answer = document.RootElement.TryGetProperty("response", out var value) ? value.GetString() : null;
            if (!string.IsNullOrWhiteSpace(answer)) return new EventInsight("Local Ollama / " + ModelName, answer.Trim());
        }
        catch (Exception) { }

        return new EventInsight("Offline diagnostics (Ollama not available)", ExplainOffline(eventText));
    }

    private static string ExplainOffline(string eventText)
    {
        var value = eventText.ToLowerInvariant();
        if (value.Contains("10013") || value.Contains("access denied") || value.Contains("administrator"))
            return "Valószínű ok: a művelethez nincs rendszergazdai jogosultság (raw packet capture vagy Windows tűzfalszabály). Ellenőrizd a Rendszer és latency fület, majd csak a szükséges művelethez indítsd adminisztrátorként az alkalmazást.";
        if (value.Contains("mac changed") || value.Contains("arp"))
            return "Lehetséges ok: az eszköz hálózati kártyát vagy routert cserélt, illetve az ARP-tábla változott. Hasonlítsd össze a gateway MAC-címét a router címkéjével és az előző ismert értékkel; ez önmagában nem bizonyít támadást.";
        if (value.Contains("dns review") || value.Contains("dns"))
            return "Lehetséges ok: CDN, terheléselosztó vagy normál DNS-rotáció miatt változott a cím. Nézd meg, ugyanaz a folyamat és domain ismétli-e a változást; egyetlen eltérő válasz nem bizonyít DNS-mérgezést.";
        if (value.Contains("rdp"))
            return "Új távoli asztali kapcsolat jelent meg. Ellenőrizd a forrás IP-t és hogy vártad-e a kapcsolatot; ha nem, vizsgáld át a Windows RDP és router port-forward beállításait.";
        if (value.Contains("device left") || value.Contains("device") || value.Contains("host"))
            return "Az eszköz nem válaszolt az utolsó helyi hálózati vizsgálatra. Lehet, hogy alvó állapotba került, levált a Wi-Fi-ről, vagy tűzfal szűri a lekérdezést; ismételd meg a vizsgálatot, mielőtt következtetést vonsz le.";
        if (value.Contains("wi-fi") || value.Contains("bssid") || value.Contains("ssid"))
            return "Az azonos SSID több BSSID-n normális lehet mesh vagy vállalati Wi-Fi esetén. Ellenőrizd, hogy a hozzáférési pontok a saját hálózatodhoz tartoznak-e, és a titkosítás WPA2/WPA3-e.";
        if (value.Contains("nmap"))
            return "Az Nmap hiányzik vagy nem futott le. Ellenőrizd, hogy telepítve van-e és elérhető-e a PATH-on; az alap ping- és TCP-portvizsgálat ettől függetlenül működik.";
        if (value.Contains("new app"))
            return "Egy új folyamat hálózati forgalmat indított. Ellenőrizd a folyamat nevét és futtatható fájljának útvonalát, majd nézd meg a Usage history nézetben a célhostot és az adatirányt. Az új aktivitás önmagában nem rosszindulatú.";
        return "Vizsgáld meg az esemény időpontját, folyamatát, célhostját és hogy ismétlődik-e. Egyetlen hálózati jelzés ritkán elég az ok vagy támadás megállapításához; ellenőrizd a Windows naplókat és a folyamat fájlútvonalát is.";
    }
}

public sealed record EventInsight(string Source, string Explanation);