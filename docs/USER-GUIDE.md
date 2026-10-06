# Community user guide — 1.1.0-beta.2 source preview

Updated 6 October 2026. This guide describes the current source preview.
The downloadable beta.1 package retains its earlier navigation and experimental VPN
implementation; its release assets and MIT rights are unchanged.

## Find your task

| Task | Current page |
| --- | --- |
| Adapter rates and CPU/RAM | Dashboard |
| Effective network path, adapters, routes, interface metrics | Network |
| Application traffic, sockets, packets, IP/software sources | Inspect |
| Requested host/port/service probes, HEX samples | Scan |
| Local events and confirmed application firewall rules | Security |
| Pasted/imported message, attachment and exposure-report review | Email |
| Retained traffic and exports | History |
| Diagnostics, appearance and monitoring preferences | Settings |
| Offline documents, origin story and creator links | About |

Community observes existing tunnels; it does not manage VPN connections.
Start passive monitoring normally. Packet capture and confirmed Windows Firewall
changes require elevation. Active probes and configuration changes are explicit actions.

## Protection and graphics

Open **Security → Firewall** for application rules, **Settings → Appearance & Monitoring** for alert/graphics preferences, and **Email → Inspector & Sender Rules** for local email review. **App rules** in Live traffic transfers the selected executable to this view.
Rules and preferences are stored in `%LOCALAPPDATA%\Pulsatilla\protection.json`.

The footer theme selector starts in **Dark** for a new profile. **Light** changes the whole
interface and rain palette; **Windows** follows the Windows app-color preference and updates
during the session. The selection persists with protection preferences. The **About** tab
includes the complete README, origin story, MIT license, service roadmap and privacy notes inside the EXE,
so they remain available offline.

The Dashboard graph uses selected-adapter byte counters sampled every 500 ms; it does not
require Administrator rights or packet capture. Amber and purple distinguish download and
upload. Choose 1, 5 or 15 minutes, hide a series, freeze the visible timeline while collection
continues, or point at the graph to inspect a timestamp and rates. Changing adapters clears
this timeline. Its history is capped at 1,800 samples and its drawing is cached between samples;
hidden charts collect values without rebuilding drawing geometry. Long sampling gaps appear
as breaks, and volume summaries integrate the observed counter-rate intervals.

In **Inspect → Traffic Sources**, top IP sources retain their packet-count ranking. Expanding an IP
shows protocols, source ports, byte totals and associated local endpoint owners. A second
**Top software sources** section ranks applications by captured bytes, with remote IP details.
This requires packet capture. Names can identify this PC, an observed LAN device/MAC, or a
DNS label resolved asynchronously on expansion. A DNS label is not verified device identity;
remote software cannot be identified from ordinary IP packets. Aggregation is bounded to
1,024 IPs and 256 applications; inaccessible endpoint owners remain `Unattributed`.

**Trust / whitelist** suppresses routine new-app notices for the exact executable path;
it does not bypass attack checks or grant additional Windows Firewall permissions.
**Block network / blacklist** explicitly installs inbound and outbound Windows Firewall
block rules, requiring Administrator rights and confirmation. **Monitor / remove block**
removes Pulsatilla's block rules and restores routine notices. Rules configured from Usage
history also update the application policy list. Path-based trust does not verify a file's
publisher, signature or hash. A captured packet from a blacklisted app is a review signal,
not evidence that the firewall allowed it through.

Routine DNS answer rotations are disabled by default. The optional rotation setting writes
ordinary event notices; it does not put CDN/load-balancer changes in the security-alert list.
With packet capture running, the attack monitor reviews 12 distinct inbound TCP SYN ports,
150 inbound SYN packets, or 20 RDP connection attempts from one source in a 15-second window.
Each signal has a two-minute cooldown. A public-to-private DNS answer transition within ten
minutes is a possible rebinding indicator. Split DNS, VPN routing, authorized scans and
reconnecting clients can produce similar signals. Packet capture cannot prove failed logins
or attacks, and encrypted DNS/IPv6 are outside this raw IPv4 detector.
Repeated unchanged mixed public/private answers and older DNS observations do not trigger
rebinding notices. Routine DNS review levels never enter the security alert stream.

The UI analysis queue is capped at 4,096 packet observations and a drain pass uses at most
200 samples / about 12 ms before yielding. Backlog loss is reported visibly; if samples are
skipped, traffic totals and attack indicators can be incomplete. Per-source/domain heuristic
state is bounded. Packet capture parsing runs off the UI thread; routine packet batch messages
no longer rebuild the events list every 150 ms.

Matrix Rain defaults to **Eco**: 32 character streams share just **three animated layer
transforms**, replacing 36 individually updated transforms. **Balanced** uses 48 streams and
four layers; **Off** removes the scene and motion clocks. WPF's timing engine moves the retained
layers without an application frame timer or render callback. Different layer speeds, varied
trail lengths, fading tails and bright leading characters create depth. Two copies of each
cached strip wrap seamlessly; glyphs are rasterized once and never regenerated during animation
or resize. A single palette uses 432 KiB of pixel data; bitmap dimensions
stay at 18 × 144–240 DIPs, including at 4K window sizes. No window-sized bitmap cache is used.
The light theme builds its own palette on first use; each palette is 432 KiB, shared between
windows, with a combined 864 KiB maximum after both themes have been used.

Eco requests 15 fps and Balanced 24 fps through WPF's
[DesiredFrameRate](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.animation.timeline.desiredframerate),
which is a timing guideline rather than a guaranteed display rate. Inactive windows request
5 fps; software rendering limits the request to 10 fps. Hidden, minimized, unloaded and
Windows reduced-animation states detach all motion clocks. Height changes preserve animation
time and normalized position; width changes retain the existing clocks. Traffic adjusts a
bounded clock speed without restarting streams. Red alerts use a separate one-shot expiry
timer, which is also stopped when hidden. The layer, stream and texture budgets stay fixed
when maximized; actual CPU/GPU cost still depends on display DPI and the rendering device.

Email review inspects explicitly pasted/imported content without accessing a mailbox, opening
links or sending data to a provider. Optional recipient filters apply only to this local review;
sender lists match exact email addresses or domains. Whitelisting never overrides risky content.
Checks cover sender/reply-to mismatch, deceptive link labels, IP/HTTP/IDN/shortened URLs,
requests for secrets, urgent payments, remote access and executable attachment names. Language
patterns currently cover common English, Hungarian and German cues. Imported messages decode
common base64/quoted-printable bodies, multipart plain/HTML text and encoded subject headers
locally, following [MIME body encoding](https://www.rfc-editor.org/info/rfc2045/)
and [encoded-header](https://www.rfc-editor.org/info/rfc2047/) conventions. Invalid encoding,
encrypted content and parser limits produce review notices. HTML-obfuscated text, nested
deceptive links and sign-in approval lures are checked. Attachment contents are not opened;
only available filenames are reviewed. Messages are not saved; only filter preferences persist.
This is advisory review, not automatic mailbox filtering, antivirus,
sender authentication, a VPN or a dark-web/breach-monitoring service.

**Local exposure reports** accepts an explicitly selected CSV with `email,service,date` columns
(`service`/`date` optional), or a JSON array of objects with those keys. Configure watched email
addresses in the email-review filters first. Only matching addresses and supplied service/date
labels are displayed; password fields and other columns are excluded. Reports are not saved
or fetched online. Limits are 1 MiB of text, 10,000 records and 100 displayed distinct matches.
Matches describe the supplied report; no match cannot establish that an address was never exposed.

Run the isolated protection checks (no firewall changes or network requests):

```powershell
dotnet run --project .\tools\Protection.Checks\Protection.Checks.csproj -c Release
```

The checks cover packet parsing, heuristic thresholds/expiry/cooldowns/bounds, concurrent
queue limits, sender/recipient rules, phishing indicators, saved policies, fixed rain budgets,
native clock movement, loaded/unloaded resize continuity, frozen sprite dimensions/cache reuse,
pixel continuity across layer loops, one-hour clock progress, bounded traffic speed, alert expiry,
Off/minimized/hidden/unloaded suspension and reload,
actual Live traffic dropdown contrast (normal/selected TCP/UDP/ICMP >=7:1) and rendering the
actual Protection layout at minimum width.
Additional checks cover MIME phishing, malformed encodings, IDN sender rules, MFA approval
lures, local report filtering, IP/software byte attribution, timeline capacity/freeze/ranges,
creator links and rendering Dashboard/Sources/About in both themes without system operations.

