# Pulsatilla Community roadmap

## M2026-10-EDITION-SPLIT — 6 October 2026

Community beta.2 removes VPN management while retaining all passive tunnel/network
inspection. Core composes private management/security modules over the shared MIT UI.
Edition capability checks, static gold styling and separate synthetic UI tests are
implemented. Local previews are being packaged; real VPN/OS/DPI acceptance is separate.
Published beta.1 history and MIT rights remain unchanged. See the
[edition report](architecture/PULSATILLA-EDITION-REPORT.md).


## M2026-10-NETWORK-FIRST — Network Path to Inspect Traffic

Opened **6 October 2026**. Latest owner-requested product milestone, shared with the
Core planning milestone. Progress entries report evidence, not predicted completion dates.

| Stage | Status | Evidence / next work |
| --- | --- | --- |
| Existing .NET 8 product checkpoint | PASS | Release: zero warnings/errors; existing protection and 18-language catalog checks passed. |
| Navigation and feature mapping | PASS | [UI rework plan](architecture/UI-NAVIGATION.md). |
| Navigation skeleton and adapter scrolling | PASS (synthetic scope) | Preserved controls; 300 geometry/theme/language combinations and 30 representative renders; [phase reports](architecture/WORKFLOW-PHASE-REPORTS.md). |
| Native classification, routed AUTO selection, network path | IMPLEMENTED / integration in progress | Native Windows route evidence; no fabricated underlay. Network checks: 51 synthetic and four optional read-only native checks passed. |
| Shared passive inspection and endpoint-aware traffic | IMPLEMENTED / integration in progress | One session; explicit start/stop; 18 lifecycle checks passed, including 32 concurrent start attempts. |
| Routes, metric readback and undo | IMPLEMENTED / real mutation NOT TESTED | Synthetic checks cover readback/rollback; no automatic privileged changes. |
| Local hashes/signatures, HEX patterns, email attachments, Defender | PASS (synthetic scope) | 47 analysis and nine bounded import checks passed; async import/paste/drop and shared policy-operation guard integrated. No live Defender scan in checks. |
| Active service-banner inspection | IMPLEMENTED / real network NOT TESTED | 26 injected-stream checks passed; explicit target confirmation, one connection, bounded read. |
| Secure OpenVPN profile/lifecycle and manual privacy checks | EXPERIMENTAL / real VPN NOT TESTED | 34 synthetic/profile/process checks passed; installed-provider prerequisites; no anonymity/kill-switch claims. |
| Final integrated build and recovery acceptance | PASS (synthetic scope) / hardware NOT TESTED | Full offline Release validation passed: zero warnings/errors, 300 workflow geometry cases/30 renders and 390 new-host assertions/210 renders. Actual Windows DPI/hardware/sleep/VPN acceptance remains separate. |

Community stays **C# / .NET 8 / WPF**, preserving its existing MIT rights, features and
creator/original-project attribution. No private commercial code is copied here.

Core development continues separately: a private baseline audit/reproducible rollback
reference precedes a proposed C++20/23 + Qt 6 + CMake implementation. Dedicated future
Core stages include WFP recovery/enforcement, evaluated encrypted P2P protocols, peer
identity and verification, LAN chat, remote discovery, channels and file transfer.
These are **roadmap items**, not available Community functionality. Community adds no
unfinished secure-chat UI or advanced WFP enforcement in this milestone.

Development uses targeted local builds/checks and the existing authenticated coding tools.
No paid service, license acceptance or visibility change is automatic.

### Latest validation checkpoint — 6 October 2026

The private Core SDK-resolution follow-up belongs to this shared milestone: its
system `dotnet.exe` now finds the project-local .NET 10 SDK. Fresh Core validation
and private development packages passed. Community continues using .NET 8.
An earlier execution quota interruption was resolved; final Community offline
validation completed successfully on 6 October. The owner authorized the public
**1.1.0-beta.1** Community prerelease and a separate private Core development dist.
Passing synthetic checks does not establish real VPN, privileged mutations or
hardware acceptance. Stable Core release and future native features remain gated.
