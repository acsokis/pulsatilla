# Origin story

## From a personal project to Pulsatilla

[Göldner Iván, Hungary](https://www.facebook.com/goldnerivan#) created the original **netw_tool_v05** as a personal home-network project for his own use. His Python application supplied the starting point: adapter information, traffic visualization, scanning, network-event review and a Matrix-style background.

**Gábor Kocsis, based in Germany, working as Gabor Web**, contributed the further product ideas and directed the redevelopment into **Pulsatilla** in **C# / WPF on .NET 8**. This work expanded application-level traffic visibility, local security and email review, and improved rendering architecture, bounded data collection, themes, diagnostics, validation and release preparation.

These roles are credited from Gábor Kocsis's account. The archive itself contains no author metadata, so this inspection does not independently prove authorship or establish who typed each line. Pulsatilla's development workflow also used AI coding assistance.

## What changed

- Original: Python with PyQt6, Scapy, NumPy, pyqtgraph and OpenGL. Pulsatilla: a Windows C# / WPF application with no third-party runtime packages; Nmap and Ollama remain optional integrations.
- The original Matrix animation used a 16 ms timer and rebuilt point arrays in its drawing callback. Pulsatilla uses cached sprite layers and native WPF motion, fixed drawing budgets, quality settings and hidden/minimized-window suspension. These are verified design changes; no comparative CPU/FPS benchmark was performed.
- Original adapter graphs, scanning, packet inspection and ARP/DNS review informed the new application. Pulsatilla adds a bounded throughput timeline, expandable IP/software sources, local process attribution, confirmed firewall rules and persistent settings/history.
- Routine DNS rotation is quiet by default. Scan/SYN/RDP/rebinding observations remain review signals, rather than proof that an attack occurred.
- Email/phishing and imported exposure-report review are local additions. The current application has no live dark-web lookup, VPN or mailbox integration.

## Dates supported by the files

- Original Python file timestamps in the supplied ZIP: **2025-11-26 to 2025-11-28**. They support a late-November 2025 snapshot, not a verified project start date.
- Earliest to latest source-file timestamp: **28 hours, 11 minutes**. This is a span between saved files, not hours worked.
- Pulsatilla's first public Git import: **2026-10-05**. This repository has no earlier development history, so the date of the C#/WPF port's beginning cannot be established from it.

ZIP timestamps have no reliable original timezone and can be changed when files are copied or archived. Neither timestamps nor source size reveal actual engineering time.

## Effort estimates

Actual working hours were not recorded in the supplied archive or public repository. A separate technical review describes reconstruction estimates and their assumptions; those estimates are not either contributor's timesheet.

- Recreating the original Python prototype: approximately **20–80 engineering hours**.
- Redeveloping the C#/WPF application with the inspected additions, validation and packaging: approximately **60–200 engineering hours**.

These broad estimates assume an experienced developer using existing frameworks and an available reference, with development-machine verification. AI assistance, prior components and debugging conditions can change the effort substantially. Long-term support, formal security testing and future paid services are excluded.

## Original reference and permissions

The inspected reference is the supplied **netw_tool_v05.zip**. No public upstream project URL or license notice was found in it. Göldner Iván's Facebook profile above was supplied separately by Gábor Kocsis; it is a contributor link, not an upstream source repository. The ZIP's SHA-256 fingerprint is **90e7e063aca3b2096ed1695c2faf350a341cd30475227f368b601c62efe1c1e5**.

The original archive is not redistributed with Pulsatilla. This credit does not assign ownership or relicense that original work; the current MIT notice cannot establish permission for the supplied archive. Reuse permission remains to be documented with the original creator.

[Read the archive comparison and dating evidence](https://github.com/acsokis/pulsatilla/blob/main/docs/ORIGIN-REVIEW.md).
