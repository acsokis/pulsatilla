# Pulsatilla commercial overview

Pulsatilla is a Windows network-monitoring and local security-review application by
**Gábor Kocsis**, based in Germany, under the **Gabor Web** company name.
The Community core is MIT-licensed and requires no account, payment or activation.
Optional paid services are a development direction. There is no active checkout,
subscription, external exposure provider or published service price.

Voluntary development support: [PayPal.Me / gaborcarter](https://paypal.me/gaborcarter).
This creator-provided link is available in GitHub funding, the README and About. Supporters
choose their amount through PayPal; this is not a feature unlock, service purchase or support
contract. The application handles no payment data and makes no charitable/tax claim.

Project: https://github.com/acsokis/pulsatilla
Contact for feedback and pilot discussions: https://www.linkedin.com/in/gabor-web/

The repository is public; publication status is tracked in
[copy notes](COPY-NOTES.md). This document is a commercial plan, not a sales contract.

## People to speak with first

| Audience | Concrete need to explore | Useful demonstration |
| --- | --- | --- |
| Windows users and freelancers | Understand unexpected network activity and identify which local app is involved | Selected-adapter chart, capture details and exact-path application rules |
| Small teams without a dedicated network specialist | Help interpreting a workstation's connections and reviewing local indicators | A guided diagnostic session and an explanation of the findings and limits |
| IT consultants and support technicians | A documented desktop workflow for client diagnostics | Capture, filter, application attribution and sanitized JSON/CSV reports |
| Developers and internal IT teams | Adapt reports or integrate a specific workflow | MIT source, report formats and the optional provider interface |

These are prospective audiences, not existing customers or verified demand. Ask about
their actual workflow, permission to inspect a device, desired outcome and willingness to
pay for a clearly scoped service before building a recurring product.

## Monetization priorities

The priority below is an assessment based on the implemented features and the unfinished
backend. It is not a revenue forecast. Every option remains a proposed service.

| Priority | Proposed service | Deliverable to define | What must exist before selling |
| --- | --- | --- | --- |
| 1 | Assisted setup and network diagnostics | Configure the selected adapter/capture workflow, explain observations and provide a sanitized diagnostic summary | Scope, customer authorization, access/privacy procedure, terms and a support contact |
| 2 | Training and support | Guided use of traffic views, application rules, local email review and report export | Session/support boundaries, availability, exclusions and accurate service terms |
| 3 | Custom integrations and deployment help | A client-specific report workflow or desktop deployment configuration | Written acceptance criteria, tested delivery, maintenance scope and code-license agreement |
| 4 | Managed scheduled reports | A separately operated reporting service with agreed retention and delivery | Working backend, access control, data handling, monitoring and cancellation process |
| 5 | Optional provider-backed exposure service | Opt-in checks for email addresses whose ownership has been verified | Actual provider and permitted use, server-side ownership verification, consent, coverage, retention, authentication and billing |

Start by discussing one narrowly defined setup or diagnostics pilot. Learn which work a
customer needs repeatedly before defining a support plan. Repeated service demand could
inform a later reporting backend; do not describe that backend as available today.

Possible public positioning:

**EN:** “The MIT Community app is available without an account. I’m exploring separate
setup, diagnostics, training and integration services. Contact me to discuss a pilot;
service scope, pricing and terms will be agreed before any paid engagement.”

**DE:** „Die MIT-Community-App ist ohne Konto nutzbar. Ich prüfe separate Leistungen für
Einrichtung, Diagnose, Schulung und Integrationen. Kontaktiere mich für ein Gespräch über
ein Pilotprojekt; Leistungsumfang, Preis und Bedingungen werden vor einem kostenpflichtigen
Auftrag vereinbart.“

## How the current product supports that plan

- The Dashboard reads selected-adapter byte counters; it can demonstrate traffic without
  Administrator rights or packet capture.
- Elevated raw IPv4 capture supplies flows and local application attribution. Attribution
  uses Windows endpoint-owner tables and can miss very short connections.
- Application block/unblock actions require confirmation and Administrator rights. Trust
  is exact-path based; it does not verify publishers or bypass attack review.
- Email review analyzes explicitly pasted/imported content locally. It does not read or
  automatically filter a mailbox.
- Imported CSV/JSON exposure reports are filtered to watched email addresses locally.
  They do not perform an external breach lookup or establish that an account is clean.
- Local security heuristics produce indicators for review, with documented coverage limits
  and possible false positives. Do not sell guaranteed protection or an attack verdict.

## Open source and paid work

MIT permits others to modify, distribute and sell the code while retaining the required
license notice. The creator also retains the right to offer paid support, custom work and
separate hosted services. This license does not reserve exclusive resale rights or prevent
forks. Customers pay for an agreed service and expertise; the current Community functions
do not become a subscription entitlement.

Any future hosted integration must enforce access on its server. Provider master keys,
payment secrets and customer credentials must not be shipped in the open-source client.
Current exposure-provider interfaces are preparation only: no provider is registered and
the UI does not dispatch external lookups.

Use [the service roadmap](../MONETIZATION.md), [privacy/data flow](../PRIVACY.md) and
[legal and wording notes](../LEGAL_AND_WORDING.md) when turning a validated pilot into an
actual offer. Prices, seller information, customer rights and service terms require review
for the specific transaction. No promised earnings, customer counts or service launch date
are included in this plan.
