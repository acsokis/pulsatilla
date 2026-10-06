# Pulsatilla Community roadmap

## M2026-10-NETWORK-FIRST — Network Path to Inspect Traffic

Opened **6 October 2026**. Latest owner-requested product milestone, shared with the
Core planning milestone. Progress entries report evidence, not predicted completion dates.

| Stage | Status | Evidence / next work |
| --- | --- | --- |
| Existing .NET 8 product checkpoint | PASS | Release: zero warnings/errors; existing protection and 18-language catalog checks passed. |
| Navigation and feature mapping | PASS | [UI rework plan](architecture/UI-NAVIGATION.md). |
| Navigation skeleton and adapter scrolling | IN PROGRESS | Preserve existing controls and action handlers. |
| Native classification, routed AUTO selection, network path | PLANNED | Native Windows route evidence; no fabricated underlay. |
| Shared passive inspection and endpoint-aware traffic | PLANNED | One session; explicit start/stop; separate active scanning. |
| Routes, metric readback and undo | PLANNED | No automatic privileged changes. |
| Local hashes/signatures, HEX patterns, email attachments, Defender | PLANNED | Bounded local processing; explicit external/system actions. |
| Secure OpenVPN profile/lifecycle and manual privacy checks | PLANNED | Installed-provider prerequisites; no anonymity/kill-switch claims. |
| UI/network/recovery acceptance matrix | NOT TESTED | Record synthetic and real-machine results separately. |

Community stays **C# / .NET 8 / WPF**, preserving its existing MIT rights, features and
creator/original-project attribution. No private commercial code is copied here.

Core development continues separately: a private baseline audit/reproducible rollback
reference precedes a proposed C++20/23 + Qt 6 + CMake implementation. Dedicated future
Core stages include WFP recovery/enforcement, evaluated encrypted P2P protocols, peer
identity and verification, LAN chat, remote discovery, channels and file transfer.
These are **roadmap items**, not available Community functionality. Community adds no
unfinished secure-chat UI or advanced WFP enforcement in this milestone.

Development tooling: Ruflo routing and Codex implementation/review are available in this
session. A callable Copilot/Codepilot integration is unavailable; its execution is not claimed.
No paid service, license acceptance, visibility change or public release is automatic.
