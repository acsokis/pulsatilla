# Community UI acceptance evidence

The workflow shell preserves existing named controls and operation delegates. `ResponsiveWorkflowShell.Apply(window, MainTabs)` runs after workflow reparenting. It is idempotent and sets the minimum window to 640 × 360 device-independent pixels (DIP). Header actions and footer selectors wrap; a compact viewport uses horizontally scrollable top navigation and tighter tab padding. Larger viewports retain a vertically scrollable sidebar. Nested section headers also scroll horizontally rather than hiding final sections.

Initial window dimensions are clamped to the primary Windows logical work area so the historical 1500 × 900 default does not force an oversized startup window. If that work area is smaller than the 640 × 360 design minimum, the minimum is reduced to fit rather than forcing inaccessible window chrome. Pure size fixtures cover a 683 × 360 work area, a 500 × 300 area below the design minimum, automatic dimensions and rejection of an empty work area. These are mathematical fit checks; usability below the design minimum and actual mixed-monitor startup placement remain unvalidated.

Selected content remains constrained to a star-sized row. The helper does not put a vertically unbounded scrolling parent around DataGrids. Existing tables retain their native scrolling/virtualization. Legacy horizontal action panels wrap; named panels remain in the tree so pending-operation disabling still affects their buttons. Adapter selection explanations become keyboard-expandable in compact windows; the actual adapter list keeps a finite scrollable viewport. Existing adapters and settings are preserved.

## Synthetic matrix

`dotnet run --project tools/Workflow.Checks -c Release` loads the actual MainWindow XAML with event handlers removed, without constructing MainWindow or starting native capture, processes, network requests or monitoring services.

The following **300 simulated geometry combinations pass**:

- Physical resolution inputs: 1366 × 768, 1920 × 1080, 2560 × 1440, 3840 × 2160.
- Scale inputs: 100%, 125%, 150%, 175%, 200%, converted to DIP viewport sizes.
- Themes: Dark, Light, Windows/system mode.
- Languages: English, German, Hungarian, Farsi, Urdu.

Checks verify the requested viewport fits the minimum size, selected content retains a finite usable viewport, the adapter list is bounded, its final item among 80 synthetic adapters can be scrolled into view, and header administrator action/theme/language selectors stay inside the window. Compact navigation's final About header is reachable by scrolling. Existing named action panels remain connected. Fifteen page mappings and parent shortcuts were also exercised at the existing 1120 × 700 minimum baseline.

Thirty offscreen render artifacts are produced under ignored `dist/workflow-qa`: 15 baseline adapter views and 15 smallest 683 × 384 DIP views representing 1366 × 768 at 200%. The latter are 96-DPI offscreen images of the simulated logical viewport, **not captures from real 200%-DPI displays**. The German compact image exposed excessive header height and an unreadable new expander label; both were corrected during review.

`tools/Analysis.Checks` additionally passes 47 synthetic checks, including bounded file/attachment/application controls in dark/light offscreen views, application row reuse, separate native TCP/UDP counts, native-only applications without captured bytes, duplicate elimination, bounded endpoint aggregation, cancelled-cache rejection and late/unloaded inspection result invalidation. Native inspection lifecycle is tested separately by `tools/Inspection.Checks` with injected fake capture; it does not start an OS socket session.

## Limits and remaining manual coverage

Actual hardware DPI changes, mixed-DPI monitor movement, touchpad behavior, assistive technology, native title-bar sizing, sleep/resume and interactive keyboard focus journeys are **NOT TESTED** by these fixtures. Windows/system mode resolves the current machine setting; it does not separately simulate every Windows high-contrast mode. New navigation labels remain English while existing translated controls are preserved; no complete localization/RTL editorial claim is made.

The 300 combinations validate shell/adapter geometry, not every action on every legacy page. Other page mappings and reusable analysis layouts were exercised separately. Real route/adapter combinations, VPN and Firewall enforcement have dedicated synthetic service checks and still require authorized live-system validation. No release or complete product/hardware certification is implied by these results.
