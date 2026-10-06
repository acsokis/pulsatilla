# PULSATILLA EDITION REPORT

Checkpoint: 6 October 2026. Local testing previews, not stable commercial clearance.

## PRODUCT SPLIT REPORT — implemented stage

| Item | Community beta.2 | Private Core 1.1.0-dev |
| --- | --- | --- |
| Shared network-first UI / nine common groups | Implemented | Composed inward from Community |
| Passive tunnel inventory / existing traffic | Retained | Retained |
| VPN management implementation | Absent from assembly and navigation | Experimental certificate-only OpenVPN module |
| Core credentials / profile protection | Absent | Windows-protected private workflows; credential vault is separate from connector |
| Capability registration | All six private flags false | VPN and current private integration enabled; future flags false |
| Premium presentation | No paid-feature placeholder | Static gold resources and text badges, no new animation |
| Origin / notices | Preserved | Preserved, moved beta.1 MIT VPN notice retained |
| Future WFP / split routing / WireGuard / secure P2P | Absent | Roadmap only, no dead menu |

Architecture: shared EditionCapabilities, FeatureDescriptor, WorkflowNavigation,
FeaturePresentation and small edition lifecycle/packet/event hooks. Core overrides
its branding, storage, logging, firewall and retained-history implementations while
consuming the common workflow UI. Shared source is pinned before local distribution.

## Validation evidence

Community Release build and protection/localization/network/inspection/analysis/email/
scanner checks pass. Community workflow verifies no VPN-management types, absent VPN
navigation and no instantiation of unavailable modules; 300 simulated resolution/DPI/
theme/language combinations and 30 renders. Host layout: 375 assertions and195 renders.
Core ReleaseCommercial and existing synthetic commercial checks pass. Core composed
workflow: 300 geometry/theme/language combinations and30 renders. Certificate-only
VPN/process checks:34 pass, using synthetic child processes and isolated DPAPI data.
A compact Core badge regression was found and fixed; no screenshot or model claim
replaces these assertions. Final pinned-source validation and package hashes are
recorded with the local distribution manifest.

These tests do not establish real display DPI, actual VPN connection/recovery, privileged
route/firewall mutation, installed-client combinations or clean-machine installation.
No real VPN/firewall operation or provider-backed breach lookup was run in default checks.

## Acceptance

Local feature-boundary and synthetic UI acceptance: PASS.
Stable commercial/real-OS acceptance: PARTIAL, pending the explicit manual gates above,
trusted signing and existing provenance/legal clearance. No paid service or stable
commercial release is created by this checkpoint. Published Community beta.1 assets,
history and MIT rights remain unchanged. New Core source stays private.

## Local verification workflow

Community: tools/Validate.ps1 -Offline with .NET8 SDK.
Core: tools/Validate.ps1 -Offline uses its local .NET10 SDK.
Run each preview as a normal user; passive monitoring is the starting point. Inspect
Dashboard, Network/Adapters, Inspect, Security, Email, Settings and About; compare
Dark/Light/Windows and small/maximized windows. Community has no VPN page; Core labels
its VPN experimental. Capture, scanning, firewall and VPN actions remain explicit.
