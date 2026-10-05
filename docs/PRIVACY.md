# Pulsatilla privacy and data flow

Pulsatilla Community has no telemetry, advertising, automatic breach lookup, payment processing
or account registration. No external exposure provider is configured. Imported email bodies
and exposure reports are reviewed in memory and are not deliberately persisted by these features.

## Stored on this PC

`%LOCALAPPDATA%\Pulsatilla` contains language/theme preferences, application policies,
watched addresses and sender filters, blocked-application state, seven days of captured
usage history and fourteen days of application logs. These files are not encrypted by the app.
Logs and exports may contain IPs, paths, event details or other sensitive information.
Packet bytes are not logged packet by packet. JSON/CSV/text exports go to the file chosen by the user.

On first startup, known files from the former `%LOCALAPPDATA%\NetWTool` profile are copied
when the new destination does not already exist. The old profile is preserved. Migrated
application logs retain the normal fourteen-day retention policy. Changing the project
directory never deletes either runtime profile.

## Network activity

Monitoring reads Windows counters and endpoint tables. Raw IPv4 packet capture requires
Administrator rights. Ping, subnet/port scans and other selected diagnostics send network
traffic. Expanding a source IP can request a reverse-DNS label through the system resolver.
Public-IP GeoIP lookup explicitly contacts ipapi.co; private addresses are excluded from that request.
Optional local explanations contact Ollama on `127.0.0.1:11434`; otherwise offline rules are used.
Social/documentation links open the default browser when clicked; those sites apply their own policies.
The voluntary PayPal support link also opens only on a click. Payment/account information
is handled by PayPal; Pulsatilla receives no payment status and stores no payment details.

## Removing data

Close the application before removing its profile files. The app's inbound/outbound block
rules survive app uninstall or profile-file deletion. Remove selected blocks through the
application while it still has their paths, or review named Pulsatilla/legacy rules in Windows
Firewall before deleting them. Other applications' rules must remain untouched.

## Future services

Any external exposure service needs explicit opt-in, a separate published data-flow/privacy
notice, server-side address verification and actual retention/deletion controls. None of
those future services is active in this release. This file describes implemented behavior;
it is not a completed privacy notice for a future commercial backend.
