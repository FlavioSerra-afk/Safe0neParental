namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Canonical Activity.Kind values related to policy application and (future) enforcement.
/// Keep these values stable once released; they are part of the SSOT Activity stream contract.
/// </summary>
public static class PolicyActivityEventKinds
{
    public const string PolicyApplied = "policy_applied";
    public const string WouldEnforceTimeBudget = "policy_would_enforce_time_budget";
    public const string WouldEnforceApps = "policy_would_enforce_apps";
    public const string WouldEnforceWeb = "policy_would_enforce_web";
    public const string WouldEnforceLocation = "policy_would_enforce_location";
}
