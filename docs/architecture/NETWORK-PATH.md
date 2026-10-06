# Network Path

Community reads local Windows networking through `WindowsNetworkTopology`. No Core implementation is imported.

The Network Path panel separates physical, virtual, tunnel, loopback and disconnected adapters. AUTO chooses the interface returned by Windows' local route lookup for representative IPv4/IPv6 destinations. These lookups transmit no packets. A destination-specific route can differ from the displayed default route; split routing is therefore not presented as a universal Internet path.

Selecting MANUAL changes the observation interface only. It does not change Windows routes. When that interface disappears or goes down, the service falls back to the routed operational interface. A fallback without verified routing is explicitly labeled.

The physical underlay is the selected physical interface itself or one unambiguous physical lower layer found in Windows' interface-stack table. Multiple or missing lower layers produce **Unknown**; a plausible Wi-Fi/Ethernet adapter is never invented as the VPN underlay.

The panel exposes `PathChanged`, `InspectRequested`, `CurrentSnapshot` and `SelectedAdapterId`. The window owns the single passive capture lifecycle and maps the selected ID to existing adapter controls. A selection is not an inspection session and is not an active scan.

Routes are bounded to 4096 displayed entries; adapters to 512 and individual address lists to 32. Windows route table results beyond the safety bound are rejected. Native tables are released in `finally` blocks. Data grids virtualize rows/columns; native reads run outside the WPF dispatcher.

Interface Priority requires a selected interface/family and explicit confirmation. Previous settings are recorded, Windows is read back, and failed changes attempt verified rollback. Undo refuses to overwrite settings changed outside Pulsatilla. Restore Automatic is scoped to the selected interface/family; there is no silent machine-wide metric reset or automatic elevation.

Synthetic checks cover selection, missing/removed/down interfaces, ambiguous/cyclic layers, metric overflow, verified changes, rollback and outside-change refusal. An optional `--native-read-only` check exercises native layouts without transmitting traffic or modifying settings. Actual multi-adapter transitions, VPN providers and real elevated metric changes still require the documented manual matrix.
