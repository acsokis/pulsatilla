# Pulsatilla services

Pulsatilla Community is MIT-licensed. The network dashboard, application rules,
local security indicators, email review and imported exposure reports remain available
without payment, an account or activation. There is no checkout or subscription in this release.

## Separate paid services — planned

- Assisted setup, network diagnostics, training and support supplied by the creator.
- An optional provider-backed exposure-monitoring service for explicitly verified email addresses.
- Managed scheduled reports or organizational deployment support, with a separate backend.

These are integration directions, not available subscriptions or promised release dates.
Pricing, provider coverage and service terms have not been set.

## Integration boundary

`IExposureProvider` defines an optional provider adapter. `ExposureProviderCoordinator`
requires explicit external-lookup consent, one to twenty complete email addresses,
a configured provider and cancellation support. Nothing registers a provider by default.
No lookup button dispatches this interface in the current release. Local CSV/JSON review
continues independently. Provider implementations should return only report metadata,
not passwords, email bodies, credentials or raw breach datasets.

For a hosted integration, keep provider credentials and subscription entitlements on the
server. Verify address ownership there before ongoing monitoring. Use short-lived authenticated
access, encrypted transport, request limits and auditable consent. The open-source desktop
client must never contain a payment-provider secret, a provider master key or a fake activation check.
Account access must be enforced by the service, since clients can be modified under MIT.

## Before taking payments

- Implement and test the actual service and its customer support process.
- Publish accurate coverage, limitations, prices, billing periods and cancellation details.
- Choose the seller identity, payment provider and legally reviewed service terms.
- Provide the applicable seller information, privacy notices and customer rights for the target market.
- Sign release binaries; validate the installer/update channel and publish release checksums.
- Complete native-language and accessibility review of the interfaces used for selling services.

The repository contains no sales account, payment credentials or external monitoring service.
Refer to [legal and wording notes](LEGAL_AND_WORDING.md) before launching commercial services.

## License in plain language

The creator keeps copyright. MIT permits others to use, modify, distribute and sell the code
while retaining the required notice. It permits your commercial use too, but does not reserve
exclusive sales rights or prevent forks. Income from your own hosted services, support or
custom work can be separate from the free source code. The MIT warranty/liability clauses
do not override mandatory law or replace separately reviewed paid-service terms.
