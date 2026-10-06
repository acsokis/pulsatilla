# Pulsatilla Community

**Network clarity. Security insight.**

A free, open-source Windows desktop app for seeing network activity and reviewing
local security signals. Built by **Gábor Kocsis / Gabor Web**, Germany.

[Download Windows app](https://github.com/acsokis/pulsatilla/releases/tag/v1.1.0-beta.1) ·
[User guide](docs/USER-GUIDE.md) · [Roadmap](docs/ROADMAP.md) ·
[English / German sharing kit](docs/launch/README.md) ·
[Support development](https://paypal.me/gaborcarter)

| At a glance | |
| --- | --- |
| Current source | **1.1.0-beta.2 preview**, updated 6 October 2026 |
| Published download | **1.1.0-beta.1 prerelease** — earlier UI; beta.2 binary release is not published yet |
| Platform | Windows 10/11 x64; compact download needs **.NET 8 Windows Desktop Runtime x64** |
| License / price | **MIT / free**, without an account, payment or activation gate |

![Community beta.2 Dashboard: amber download, purple upload and nine workflow sections; synthetic demo data](docs/launch/assets/screenshots/community-beta2-dashboard-dark-en.png)

*Current beta.2 WPF interface, rendered with labeled synthetic demo data.
No personal traffic, device inventory or mailbox content is shown.*

## What you can do

- **See traffic clearly:** adapter throughput, CPU/RAM, application flows, sockets,
  packet details, and expandable IP/software source rankings.
- **Understand your network path:** physical/virtual adapter classification, effective
  routed interface, routes and interface-priority controls with readback and undo.
- **Inspect when you choose:** explicit capture start/stop; requested host, port and
  service scans; optional separately installed Nmap.
- **Review local security signals:** new applications, rate-limited scan/SYN/RDP/DNS
  heuristics and confirmed Windows Firewall app rules. Routine DNS rotation is quiet by default.
- **Review email and files locally:** pasted/imported messages, sender rules, attachments,
  hashes/signatures, bounded HEX patterns and optional Windows Defender actions.
- **Keep control:** bounded history/export, Dark / Light / Windows themes, reduced-motion
  support, Eco / Balanced / Off graphics, and offline About documents.

Navigate through **Dashboard · Network · Inspect · Scan · Security · Email · History ·
Settings · About**. See the [user guide](docs/USER-GUIDE.md) for behavior and limitations.

## Start using it

Download the Windows ZIP from the linked release, verify its supplied SHA-256 checksum,
extract it and run `Pulsatilla.exe`. Install the .NET 8 **Desktop** Runtime x64 first.
The prerelease executable is unsigned. Monitoring works as a normal user; restart as
Administrator only for packet capture or an explicitly confirmed firewall action.

Security findings are indicators for review, not proof of an attack. Email/exposure
review has no automatic mailbox access or external dark-web/breach lookup.
GeoIP and optional local Ollama explanations are explicit features; see
[privacy and data flow](docs/PRIVACY.md).

## Community and Core

| | Community — free MIT source | Core — private development |
| --- | --- | --- |
| Network workflow | Nine sections, monitoring, inspection, scans and local review | Shared Community workflow plus private modules |
| VPN | Passive tunnel detection and inspection of already-connected traffic | Protected profile/credential storage; **experimental certificate-only OpenVPN connector** |
| Security additions | Local advisory signals and confirmed app rules | Application security center, real Windows signature results and private firewall ownership/recovery checks |
| Status | beta.2 source preview; beta.1 published download | **1.1.0-dev**, private and unavailable as a stable commercial release |

Core development continues separately. WFP kill switch, per-app split routing,
WireGuard management, secure P2P/messaging, advanced reports and hosted threat/breach
services are **roadmap work**. Live VPN/OS recovery, installation and commercial
clearance gates remain open. No paid subscription or checkout is active.
The previously published beta.1 VPN code retains its MIT rights and notices.

## Latest progress — 6 October 2026

**M2026-10-EDITION-SPLIT:** Community VPN management was removed, the shared UI was
composed into private Core, and capability-gated module registration was added.
Local Release builds and synthetic checks pass, including 300 UI geometry/theme/language
combinations and representative renders. These checks do not establish real display DPI,
live VPN recovery or privileged OS-operation acceptance.

See the [edition report](docs/architecture/PULSATILLA-EDITION-REPORT.md),
[feature matrix](docs/architecture/EDITION-FEATURE-MATRIX.md),
[beta.2 notes](docs/releases/1.1.0-beta.2.md) and [changelog](CHANGELOG.md).
The [current screenshot gallery](docs/launch/README.md#app-screenshots--synthetic-demonstration-data)
also includes Network Path, application traffic, source details, email review and Light mode.

## Build and contribute

Use Windows and the .NET 8 SDK with Windows Desktop targeting support:

```powershell
dotnet build .\Pulsatilla.sln -c Release
dotnet run --project .\Pulsatilla.Wpf\Pulsatilla.Wpf.csproj
.\tools\Validate.ps1 -Offline
```

[Development setup](docs/DEVELOPMENT.md) · [Contributing](CONTRIBUTING.md) ·
[Report a security issue](SECURITY.md) · [Locale notes](Pulsatilla.Wpf/Resources/Locales/README.md)

The existing 18-language catalog is retained; newer technical pages can remain English.
Translation expansion is paused. No orchestration runtime is required to build or run.

## Support and credits

Voluntary [PayPal.Me support](https://paypal.me/gaborcarter) helps development; it does
not unlock features or buy a service. Setup, diagnostics, training and integrations are
separate [planned service opportunities](docs/MONETIZATION.md). Contact
[Gabor Web on LinkedIn](https://www.linkedin.com/in/gabor-web/).

Development started **5 October 2026 at 13:55 CEST**, as supplied by Gábor Kocsis.
The complete [origin story and contributor credits](docs/ORIGIN.md) are preserved
in the repository and the application's About section.

[MIT license](LICENSE) · [Privacy](docs/PRIVACY.md) ·
[Legal and wording notes](docs/LEGAL_AND_WORDING.md)
