using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    // 16W21: Policy sync watchdog threshold.
    // Default is ~10 minutes, overrideable for local testing.
    // Docs: Docs/90_User_Manual/13_Device_Health.md
    private static TimeSpan GetPolicyWatchdogThresholdUnsafe_NoLock()
    {
        // Prefer seconds override for tests.
        var sec = Environment.GetEnvironmentVariable("SAFE0NE_POLICY_WATCHDOG_SECONDS");
        if (!string.IsNullOrWhiteSpace(sec) && int.TryParse(sec, out var s))
        {
            return TimeSpan.FromSeconds(s);
        }

        var mins = Environment.GetEnvironmentVariable("SAFE0NE_POLICY_WATCHDOG_MINUTES");
        if (!string.IsNullOrWhiteSpace(mins) && int.TryParse(mins, out var m))
        {
            return TimeSpan.FromMinutes(m);
        }

        return TimeSpan.FromMinutes(10);
    }
}
