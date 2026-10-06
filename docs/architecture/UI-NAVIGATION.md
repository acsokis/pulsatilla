# UI REWORK PLAN

Milestone: **M2026-10-NETWORK-FIRST**, opened 6 October 2026.
Scope: Pulsatilla Community, C# / .NET 8 / WPF. No private Core source is reused.

### Current problems

- Ten peer tabs mix passive capture, active probes, configuration and security review.
- Dashboard chooses the first alphabetically sorted adapter rather than the outbound route.
- Adapter inventory sits in a dashboard panel with an unconstrained list inside a scrolling grid.
- Security and Events combine capture controls, the latest packet, histogram and source trees.
- Protection hides local email and sender rules beneath unrelated firewall/graphics controls.
- System diagnostics mixes hardware, latency, capture readiness and firewall logs.
- Live Traffic currently aggregates application/host/protocol without endpoint ports or PID.

### New navigation

```text
Dashboard
Network
    Network Path
    Adapters
    Routes
    Interface Priority
    Diagnostics
Inspect
    Live Traffic
    Applications
    Connections
    Packet Inspector
    Protocol Analysis
    Traffic Sources
Scan
    Host Discovery / Port Scan / Service Detection / Optional Nmap
    HEX / Sample Analysis
Security
    Overview
    Events
    Application Security
    Firewall
    Reputation / Threat Analysis
Email
    Inspector
    Attachments
    Sender Rules
    Local Exposure Reports
History
Settings
About
```

### Primary workflow

Network Path (AUTO routed interface by default) -> Inspect Traffic ->
Applications / Connections / Packets -> Security review -> Optional active Scan.

One inspection session feeds all passive consumers. Route changes never silently start
capture or a scan. A selected interface disappearing stops capture and explains why.
Manual choice is retained only while valid; uncertain topology is displayed as Unknown.

### Existing feature mapping

| Existing feature / controls | New location |
| --- | --- |
| Dashboard throughput, freeze/range/series, system summary, alerts banner | Dashboard |
| AdapterList, IP/gateway/MAC/link details, selected adapter | Network Path / Adapters; Dashboard path summary |
| LiveTrafficGrid, text/protocol filters, app-rule shortcut | Inspect / Live Traffic |
| ConnectionsGrid TCP/UDP snapshot | Inspect / Connections |
| StartCaptureButton, StopCaptureButton, HexInspector, PacketSummaryLabel | Shared inspection actions; Inspect / Packet Inspector |
| HistogramLabel, packet counts | Inspect / Protocol Analysis |
| TopIpSourcesTree, TopSoftwareSourcesTree, hostname resolution | Inspect / Traffic Sources |
| PingSweepButton, range/target, fast/full scan, Nmap, ScanResultsGrid | Scan; explicit active operations |
| GeoIpButton / GeoIpOutput | Scan / manual target lookup, retained explicit external action |
| AlertsTab, AlertsList, local explanation and optional Ollama | Security / Events |
| EventsList, threat monitor, DNS rotation preferences | Security / Overview and Events; Settings / monitoring |
| ApplicationPoliciesGrid, choose/use executable, Monitor/Trusted/Blocked actions | Security / Firewall and Application Security |
| Email inputs/import/analyze/clear and trusted/blocked/watch filters | Email / Inspector and Sender Rules |
| ExposureResultsGrid, local report import/clear | Email / Local Exposure Reports |
| UsageGrid, periods and selected application firewall action | History / Traffic |
| Hardware/system/admin/capture readiness/errors/log directory | Settings / System; Network / Diagnostics shortcuts |
| PingLatencyChart, target and Wi-Fi security review | Network / Diagnostics |
| FirewallLogBox read-only latest records | Security / Firewall |
| RainQualityBox, themes/languages, monitoring preferences | Settings / Appearance and Monitoring; persistent selectors retained |
| AboutDocumentsTabs, creator/attribution/social/support links | About, unchanged credits and licensing |

### New feature requirements

| Feature | Priority | Complexity |
| --- | --- | --- |
| Navigation skeleton and reachable adapter controls | First | Medium |
| Native route evidence, adapter classification, AUTO/MANUAL selection | First | High |
| Network Path, shared inspection state/status and capture lifecycle | First | High |
| Endpoint-aware Live Traffic, application signature/hash/risk details | Next | High |
| Readable routes and verified/undoable interface metrics | Next | High |
| Bounded masked HEX patterns and virtualized viewer | Next | Medium |
| Packet/scanner/email attachment integration and explicit Defender action | Next | High |
| VPN management | Core only from beta.2 | Private experimental module |
| Optional quick actions | Later | Medium |
| Secure Chat and advanced WFP | Outside this Community task | Separate Core roadmap |

### Checkpoint and implementation order

Baseline: `d241903c049d099bdea0ff600a62644c1ca8c34e`, branch created from protected
Community main: `feat/community-network-workflow`. Release build: zero warnings/errors;
existing Protection.Checks and eighteen localization catalogs passed offline on 6 October.
Only the pre-existing untracked `pulsatilla.code-workspace` was dirty; leave it untouched.
Baseline log: ignored `dist/validation-workflow-baseline.log`. No capture, scan or firewall
mutation was performed. This document is the local checkpoint before implementation.

Implement navigation, scrolling, grouping, route selection, path screen, inspection/session,
live/application views, routes/metrics, HEX engine/integrations, email/Defender, then QA. VPN management continues in Core.
Use small commits and phase reports. Unavailable prerequisites must have an accurate label.
Physical hardware/DPI/sleep/VPN and privileged OS recovery tests remain NOT TESTED until run.
No public release is published automatically.
