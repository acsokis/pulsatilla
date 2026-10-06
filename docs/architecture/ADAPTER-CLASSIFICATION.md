# Adapter classification

`AdapterClassifier` uses interface type and Windows IP Helper hardware/tunnel facts. The display name alone never decides a class.

| Evidence | Classification |
| --- | --- |
| Windows loopback type | Loopback |
| Tunnel / PPP interface type or native tunnel encapsulation type | LayeredTunnel |
| Windows says non-hardware and description contains known VPN/TAP/Wintun characteristics | LayeredTunnel |
| Windows hardware flag | Physical |
| Windows non-hardware flag without tunnel evidence | Virtual |
| Missing usable native evidence | Unknown |

Operational status independently determines the **Disconnected** presentation group; the underlying hardware/virtual/tunnel classification is retained. Native properties can be incomplete or provider-specific, so classification is descriptive rather than a security guarantee. PPP includes non-VPN WAN connections and a tunnel label does not prove encrypted VPN service.

`GetIfEntry2` supplies native hardware and tunnel flags. Physical relationships use `GetIfStackTable`; an unproven relationship remains Unknown. Interface indices may change across adapter removal/re-enable, so manual selection uses the .NET adapter ID and revalidates each snapshot.

Primary API references: [MIB_IF_ROW2](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/ns-netioapi-mib_if_row2), [GetIfStackTable](https://learn.microsoft.com/en-us/windows/win32/api/netioapi/nf-netioapi-getifstacktable).
