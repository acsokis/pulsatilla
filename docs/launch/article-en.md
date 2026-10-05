# Pulsatilla: network visibility and an open-source service business

I started the Pulsatilla project on 5 October 2026 at 13:55 CEST (Germany / Europe/Berlin).

I’m developing Pulsatilla, a Windows desktop application for understanding network activity and reviewing local security indicators. My name is Gábor Kocsis, and I’m based in Germany under the Gabor Web name. Pulsatilla brings traffic charts, application activity, network diagnostics and local email review into one interface. The Community application is published as an early release with its source code on GitHub.

The Dashboard starts with the selected network adapter. Windows byte counters supply the live download and upload rates, shown in amber and purple. You can inspect a timestamp, switch between one-, five- and fifteen-minute views, pause the visible timeline and review traffic volume. This view works without Administrator rights or packet capture, making it a useful starting point for understanding a connection.

For more detail, elevated IPv4 packet capture supplies live traffic flows, protocol information and source rankings. Windows connection tables help associate traffic with local applications. Expand an IP to inspect its details, or use the separate software view to explore an application’s remote connections. Attribution can miss very short connections or inaccessible processes. Blocking an application uses Windows Firewall and requires both confirmation and Administrator rights.

The security views highlight observations that deserve investigation: MAC changes, unusual inbound connection patterns, repeated RDP connection attempts and possible DNS rebinding. Routine DNS address rotation stays quiet by default. These are review indicators, with possible false positives and coverage limits. Local email review checks explicitly pasted or imported messages for deceptive links, sender inconsistencies and social-engineering signals. Imported exposure reports can be filtered to watched email addresses; this release does not perform an external breach search or read your mailbox.

The business direction starts with an MIT-licensed Community core that requires no account, payment or activation. I’m exploring separate paid work around assisted setup, network diagnostics, training, support, custom report workflows and integrations. The value would be a clearly scoped service: helping someone configure a workflow, interpret observations or adapt the tool to a practical requirement. Pricing and service terms have not been published, and there is no active subscription or checkout.

Later possibilities include managed scheduled reports and an optional provider-backed exposure service. Those would need a working backend, verified ownership of watched addresses, consent, clear coverage and an agreed data-handling process before being offered. They remain development directions, without a promised launch date.

You can explore the project at https://github.com/acsokis/pulsatilla. If you want to support development voluntarily, the public link is https://paypal.me/gaborcarter. Support does not unlock features or create a service contract. For feedback or a discussion about a potential diagnostics, training or integration pilot, find Gabor Web at https://www.linkedin.com/in/gabor-web/.
