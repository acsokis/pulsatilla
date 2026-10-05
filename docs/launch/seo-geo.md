# Pulsatilla SEO and GEO copy

Prepared on 2026-10-05 for an English/German launch. These are reusable copy and markup
examples for a future product page; this file does not create or host a website.
The public repository is `https://github.com/acsokis/pulsatilla`, verified on 2026-10-05.
No product domain, office address or service price is assumed.

Voluntary development support is available via the creator-provided public link
[PayPal.Me / gaborcarter](https://paypal.me/gaborcarter). It does not unlock features or buy
a paid service; the app does not handle payment information. Optional paid services remain planned.

## Search and social metadata

Use the English material on an English page and German material on a German page.
The titles are descriptive suggestions; search engines may choose different title text.
Google recommends concise, page-specific titles without repeated keywords.
[Google title-link guidance](https://developers.google.com/search/docs/appearance/title-link)

| Field | English | Deutsch |
| --- | --- | --- |
| Page title | Pulsatilla — Windows Network Monitoring & Local Security Review | Pulsatilla — Netzwerkmonitoring für Windows |
| Main heading | Pulsatilla: network clarity on Windows | Pulsatilla: Netzwerkaktivität unter Windows verstehen |
| Meta description | Pulsatilla is an MIT Windows network tool with live traffic, app activity and local email review. Explore the app and planned support services from Gabor Web. | Pulsatilla ist ein MIT-Netzwerktool für Windows mit Live-Traffic, App-Aktivität und lokaler E-Mail-Prüfung. Entdecke die App und geplante Supportleistungen. |
| Social title | Pulsatilla by Gabor Web | Pulsatilla von Gabor Web |
| Social description | Live network visibility, local review and an MIT Community core. Separate setup, support and integration services are planned. | Live-Netzwerkübersicht, lokale Prüfung und ein MIT-Community-Kern. Separate Leistungen für Einrichtung, Support und Integrationen sind geplant. |

Meta descriptions should summarize the actual page. Google can build snippets from the
visible content instead and truncate them for the display; there is no fixed character
count that guarantees a particular snippet.
[Google snippet guidance](https://developers.google.com/search/docs/appearance/snippet)

## Product introduction — English

Pulsatilla is an MIT-licensed Windows 10/11 x64 desktop application for network monitoring,
application traffic and local security review. Created by Gábor Kocsis of Gabor Web, based
in Germany, it combines an adapter throughput chart, elevated IPv4 packet capture,
confirmed Windows Firewall application rules and explicitly imported email/report review.
The Community application needs no account or activation. Separate setup, diagnostics,
training, support and integration services are planned; no subscription or external
exposure service is active in this release.

## Produktbeschreibung — Deutsch

Pulsatilla ist eine MIT-lizenzierte Desktop-Anwendung für Windows 10/11 x64 zur
Netzwerküberwachung, Anzeige von Anwendungstraffic und lokalen Sicherheitsprüfung.
Entwickelt von Gábor Kocsis von Gabor Web, mit Wohnsitz in Deutschland, verbindet sie eine
Adapter-Durchsatzkurve, IPv4-Paketerfassung mit Administratorrechten, bestätigte
Anwendungsregeln für die Windows-Firewall und die Prüfung ausdrücklich importierter
E-Mails und Berichte. Die Community-App benötigt weder Konto noch Aktivierung. Separate
Leistungen für Einrichtung, Diagnose, Schulung, Support und Integrationen sind geplant;
aktive Abonnements oder externe Dienste zur Datenleck-Prüfung gibt es in dieser Version nicht.

## Consistent product facts

| Entity/property | Publishable fact |
| --- | --- |
| Product | Pulsatilla |
| Slogan | Network clarity. Security insight. |
| Company name | Gabor Web |
| Creator | Gábor Kocsis |
| Creator location | Germany; no city or physical business address has been provided |
| Platform | Windows 10/11 x64, C#/WPF on .NET 8 |
| Current framework-dependent download | Requires .NET 8 Desktop Runtime |
| License | MIT; commercial use and redistribution are permitted with the required notice |
| Community access | No account, payment or activation gate |
| Current exposure review | Local imported CSV/JSON reports; no external lookup |
| Commercial status | Separate services planned; no prices or subscriptions active |

Use “developed by a creator based in Germany” for geographic context. Do not imply a
German office address, registered company form, local service coverage or certification
that has not been established.

## Factual answer blocks — English

**What is Pulsatilla?**
Pulsatilla is an open-source Windows desktop tool for live network visibility, local
application activity, network indicators and explicitly requested local email/report review.

**How does the live graph work?**
It samples Windows byte counters for the selected network adapter every 500 ms. Amber
shows download and purple shows upload. The Dashboard does not require packet capture or
Administrator rights. Changing adapters clears its visible timeline.

**How does application traffic attribution work?**
Administrator-enabled raw IPv4 packet capture is associated with local Windows TCP/UDP
endpoint-owner tables. Very short connections or inaccessible processes can remain
unattributed. It does not identify software running on a remote device.

**Does Pulsatilla automatically block traffic?**
An application block action requires explicit confirmation and Administrator rights. It
adds inbound and outbound Windows Firewall block rules for the selected executable path.
Local indicators are prompts to review activity, not proof of a successful attack.

**Does it read my mailbox or monitor the dark web?**
The current release reads neither a mailbox nor a dark-web service. It analyzes email
content that you paste/import and reviews selected CSV/JSON exposure reports locally.
No external exposure provider is configured.

**What information stays on the PC?**
Preferences, application rules, watched addresses, seven days of captured usage history
and fourteen days of logs are stored locally. Those files are not encrypted by the app.
Explicit scans, DNS resolution and the optional public-IP lookup can generate network requests.

**Is Pulsatilla free, and what could be paid?**
The Community core is MIT-licensed with no account or activation. Setup, diagnostics,
training, support, custom integrations and later hosted services are separate planned
commercial options. There are no active subscriptions or service prices.

**Who developed it?**
Gábor Kocsis, based in Germany, developed Pulsatilla under the company name Gabor Web.
The project source and documentation are published on GitHub.

## Sachliche Antwortbausteine — Deutsch

**Was ist Pulsatilla?**
Pulsatilla ist ein Open-Source-Desktoptool für Windows, das Live-Netzwerkaktivität, lokale
Anwendungsaktivität und Netzwerkhinweise zeigt sowie ausdrücklich ausgewählte E-Mails und
Berichte lokal prüft.

**Wie funktioniert die Live-Kurve?**
Alle 500 ms werden Windows-Bytezähler des ausgewählten Netzwerkadapters abgefragt.
Download erscheint in Gelb, Upload in Violett. Das Dashboard benötigt weder Paketerfassung
noch Administratorrechte. Ein Adapterwechsel setzt die sichtbare Zeitleiste zurück.

**Wie wird Traffic einer Anwendung zugeordnet?**
Die IPv4-Paketerfassung mit Administratorrechten nutzt lokale Windows-TCP/UDP-Tabellen
mit Prozesszuordnung. Sehr kurze Verbindungen oder nicht zugängliche Prozesse können
ohne Zuordnung bleiben. Software auf einem entfernten Gerät wird dadurch nicht identifiziert.

**Blockiert Pulsatilla Traffic automatisch?**
Eine Anwendungssperre erfordert Bestätigung und Administratorrechte. Sie legt eingehende
und ausgehende Windows-Firewall-Sperrregeln für den ausgewählten Programmpfad an.
Lokale Hinweise dienen der Prüfung und beweisen keinen erfolgreichen Angriff.

**Liest die App mein Postfach oder überwacht sie das Dark Web?**
Die aktuelle Version greift weder auf ein Postfach noch auf einen Dark-Web-Dienst zu.
Sie prüft eingefügte oder importierte E-Mail-Inhalte und ausgewählte CSV/JSON-Berichte
lokal. Ein externer Anbieter zur Datenleck-Prüfung ist nicht eingerichtet.

**Welche Daten werden auf dem PC gespeichert?**
Einstellungen, Anwendungsregeln, beobachtete E-Mail-Adressen, sieben Tage erfasste
Nutzungshistorie und vierzehn Tage Protokolle bleiben lokal. Die App verschlüsselt diese
Dateien nicht. Ausdrücklich gestartete Scans, DNS-Auflösung und die optionale öffentliche
IP-Abfrage können Netzwerkanfragen erzeugen.

**Was ist kostenlos, und was könnte kostenpflichtig sein?**
Der Community-Kern steht unter MIT und benötigt weder Konto noch Aktivierung. Einrichtung,
Diagnose, Schulung, Support, individuelle Integrationen und spätere gehostete Dienste sind
separate geplante Geschäftsoptionen. Aktive Abonnements oder Leistungspreise gibt es noch nicht.

**Wer hat Pulsatilla entwickelt?**
Gábor Kocsis, mit Wohnsitz in Deutschland, hat Pulsatilla unter dem Unternehmensnamen
Gabor Web entwickelt. Quellcode und Dokumentation sind auf GitHub veröffentlicht.

## GEO and discovery guidance

Here GEO means generative-engine discovery, with the geographic facts above supplied
separately. Keep the short answers visible on the page alongside the product explanation,
real screenshots and links to the versioned documentation. Identify the creator and
distinguish implemented features from planned services consistently.

Google says its AI search features use the same SEO foundations, with no special AI files
or schema required. Important content should be readable text, pages must be accessible
for indexing, and structured data should agree with the page. Inclusion is not guaranteed.
This guidance concerns Google Search; it does not establish requirements for every other
answer engine. [Google AI search documentation](https://developers.google.com/search/docs/appearance/ai-features)

For a future website, use distinct English/German URLs with visible language links and
reciprocal `hreflang` references listing both versions. Add the real fully qualified URLs
only after hosting is chosen.
[Google localized-page guidance](https://developers.google.com/search/docs/specialty/international/localized-versions)

Track useful outcomes such as repository visits and pilot inquiries; do not describe
keyword placement, structured data or publication as a guarantee of rankings or sales.
Keep location wording factual instead of publishing duplicate city/service pages for
places where no service has been established.

## JSON-LD example for a future product page

This is syntactically valid entity markup using known facts and relative fragment IDs.
It assumes the repository URL has been confirmed public. Put the same facts in visible
page text and adapt the description for the page language. Replace the app `url` with the
actual product-page URL if one is later published; no unprovided domain is used here.

```json
{
  "@context": "https://schema.org",
  "@graph": [
    {
      "@type": "SoftwareApplication",
      "@id": "#pulsatilla",
      "name": "Pulsatilla",
      "description": "MIT-licensed Windows network monitoring with live traffic, local application activity and explicitly imported email and exposure-report review. Separate paid services are planned; no external exposure provider is active.",
      "url": "https://github.com/acsokis/pulsatilla",
      "applicationCategory": "UtilitiesApplication",
      "operatingSystem": "Windows 10 or Windows 11 (x64)",
      "softwareRequirements": ".NET 8 Desktop Runtime for the framework-dependent release",
      "license": "https://opensource.org/license/mit",
      "isAccessibleForFree": true,
      "author": { "@id": "#creator" },
      "publisher": { "@id": "#gabor-web" },
      "featureList": [
        "Selected-adapter throughput timeline",
        "Administrator-enabled IPv4 packet capture and local application attribution",
        "Confirmed Windows Firewall application block and unblock actions",
        "Local review of explicitly pasted or imported email content",
        "Local imported CSV/JSON exposure-report review"
      ]
    },
    {
      "@type": "Person",
      "@id": "#creator",
      "name": "Gábor Kocsis",
      "homeLocation": { "@type": "Country", "name": "Germany" },
      "sameAs": ["https://www.linkedin.com/in/gabor-web/"]
    },
    {
      "@type": "Organization",
      "@id": "#gabor-web",
      "name": "Gabor Web"
    }
  ]
}
```

No ratings, customer counts, paid-service offers or service prices are invented. This
example describes entities; it does not satisfy every requirement for a Google software-app
rich result. Google's feature requirements also include an offer price and a genuine rating
or review. Add such fields only when supported by actual visible information, and validate
the deployed page. [Google software-app structured-data documentation](https://developers.google.com/search/docs/appearance/structured-data/software-app)

## Caption and alt-text copy

For the supplied synthetic Dashboard capture:

- EN: “Pulsatilla Dashboard with an amber download curve, purple upload curve and traffic
  controls. Synthetic demonstration data.”
- DE: „Pulsatilla-Dashboard mit gelber Downloadkurve, violetter Uploadkurve und
  Traffic-Steuerung. Synthetische Demonstrationsdaten.“

For a generated launch illustration:

- EN: “Pulsatilla launch illustration by Gabor Web; decorative network imagery.”
- DE: „Pulsatilla-Launchillustration von Gabor Web mit dekorativen Netzwerkmotiven.“

Use captions that match the actual image. A decorative illustration must not be described
as evidence of app behavior. See [copy notes](COPY-NOTES.md) for publication handling.
