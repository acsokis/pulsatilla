namespace Pulsatilla.Wpf;

/// <summary>Routine DNS changes never enter the security alert stream.</summary>
public static class SecurityEventPolicy
{
    public static bool IsAlarm(string level) => level.Trim().ToUpperInvariant() is
        "ERROR" or "CRITICAL" or "ALERT" or "FIREWALL ERROR" or "RDP" or "DNS ANOMALY" or
        "ATTACK REVIEW" or "BLOCKLIST REVIEW" or "WI-FI REVIEW" or "DEVICE LEFT";
}
