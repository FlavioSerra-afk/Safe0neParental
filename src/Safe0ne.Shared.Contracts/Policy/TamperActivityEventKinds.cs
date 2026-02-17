namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Canonical Activity.Kind values related to agent tamper / circumvention signals.
/// Keep these values stable once released; they are part of the SSOT Activity stream contract.
/// </summary>
public static class TamperActivityEventKinds
{
    public const string AgentNotElevated = "tamper_agent_not_elevated";
    public const string AgentEnforcementError = "tamper_enforcement_error";

    public const string CircumventionVpnSuspected = "circumvention_vpn_suspected";
    public const string CircumventionProxyEnabled = "circumvention_proxy_enabled";
    public const string CircumventionPublicDnsDetected = "circumvention_public_dns_detected";
    public const string CircumventionHostsWriteFailed = "circumvention_hosts_write_failed";
}
