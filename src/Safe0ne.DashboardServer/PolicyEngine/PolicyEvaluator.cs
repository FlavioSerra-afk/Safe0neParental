using Safe0ne.Shared.Contracts;
using Safe0ne.Shared.Contracts.Policy;

namespace Safe0ne.DashboardServer.PolicyEngine;

public static class PolicyEvaluator
{
    public static EffectiveChildState Evaluate(ChildPolicy policy, DateTimeOffset nowUtc)
    {
        // Delegate to the shared pure engine so Kid and Server converge on the same rules.
        return EffectivePolicyEngine.Evaluate(policy, nowUtc);
    }
}
