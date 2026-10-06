# Pulsatilla UI design system

Shared implementation: WPF workflow navigation, responsive shell, controls and themes.
Community/Core differences are registered feature modules, not a duplicate desktop shell.

## Themes and accessibility

Dark is the default. Light and Windows theme selections use dynamic resource brushes.
High-contrast mode uses Windows control/text brushes for premium accents. Keep text
labels alongside color, keyboard focus and control names; gold must not encode status.
CoreAccentBrush: dark #C8A45D, light #765618. CoreBorderBrush: dark #A58A54,
light #9A7736. CoreHeaderBrush: dark #242016, light #FAF3E6.
CoreGlowBrush is transparent. No animated border, shadow or glow is introduced.
Traffic colors and security severity meanings remain separate from edition styling.

## Feature composition

EditionCapabilities owns six flags: VpnManagement, AdvancedThreatIntel, AdvancedWfp,
SecureMessaging, CommercialIntegrations and AdvancedReports. FeatureDescriptor carries
id, name, required capability, Community/Core/ExperimentalCore tier, icon and group.
Registration checks capability before constructing a page or service; unavailable
modules add no dead menu or placeholder. Community's six flags are all false.
A Core feature gets a static text badge and resource-based border/header; experimental
features say CORE · EXPERIMENTAL. Keep badges compact so high-DPI navigation scrolls.

## Layout and graphics

Wide views use side navigation. Compact views use scrollable horizontal tab strips.
Adapter lists and tables retain bounded viewports; actions remain scroll reachable.
Matrix rain retains a fixed sprite/cache budget, quality settings and minimized pause.
Gold edition styling adds no animation. Test Dark/Light/Windows, compact and wide
work areas, scrolling and representative content with synthetic fixtures.
DIP/resolution simulation does not certify physical display DPI or live OS operations.

## Source direction

Private Core consumes a pinned public MIT baseline inward. Private modules never flow
into Community. Preserve published MIT history and notices. The application has no development-orchestration runtime dependency.
