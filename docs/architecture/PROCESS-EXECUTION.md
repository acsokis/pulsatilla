# Community subprocess execution audit

Network inspection itself does not spawn independent capture subprocesses. Existing explicit and read-only Windows subprocess workflows now share `SafeProcessRunner`:

| Workflow | Executable resolution | Deadline | Retained output |
|---|---|---|---|
| Current Wi-Fi details | Absolute Windows System32 `netsh.exe` | 3 seconds | 64 KiB per stream |
| Nearby Wi-Fi review | Absolute Windows System32 `netsh.exe` | 8 seconds | 128 KiB per stream |
| Hardware CIM inventory | Absolute System32 `WindowsPowerShell/v1.0/powershell.exe` | 15 seconds | 256 KiB per stream |
| Optional explicit Nmap | Standard installed Program Files locations or explicit selected local `nmap.exe` | 2 minutes | 256 KiB per stream |
| Existing confirmed application firewall policy | Absolute Windows System32 `netsh.exe` | 15 seconds | 64 KiB per stream |

Nmap does not resolve executables through PATH or the working directory. The selected local executable rejects network/mapped/reparse paths; users confirm the actual target, executable path and `-sV -O` arguments before an active scan. Targets are literal IP addresses, not hostnames, ranges or ambiguous IPv4 shorthand. Nothing downloads or installs Nmap automatically. This runner does not execute a shell command.

All runner arguments use `ProcessStartInfo.ArgumentList`. Hardware inventory passes a constant reviewed PowerShell query, with no user-input interpolation; UTF-8 output is selected explicitly. Both stdout and stderr are drained concurrently while retained preview sizes remain bounded. Incomplete/truncated hardware and Wi-Fi results are marked unavailable, never interpreted as a clean result. Nmap output truncation is clearly labeled.

Each window workflow suppresses overlapping copies of its own operation. `MainWindow.DisposeWorkflowProcesses()` cancels the shared lifetime token on window shutdown. Cancellation/deadline termination applies to the process tree started for that operation, not arbitrary process names or external VPNs. Process error logs contain generic operation context, not raw command output or profiles.

OpenVPN has a separate persistent owned-process lifecycle and authenticated management connection, documented in [VPN integration](VPN-INTEGRATION.md). Normal disconnect first asks its own OpenVPN instance for SIGTERM and then has a bounded forced-termination fallback; actual route restoration remains an explicit manual-validation item.

`tools/Vpn.Checks` verifies synthetic child PID termination after deadlines and cancellation, concurrent stdout/stderr saturation, retained output limits and shell metacharacters remaining literal. Tests do not launch real Nmap/netsh/PowerShell scans or mutate Windows network configuration. Actual installed tools, localized netsh output and administrator behavior require manual OS checks. Existing localized Wi-Fi string parsing remains a known limitation, rather than a claim of language-independent security detection.
