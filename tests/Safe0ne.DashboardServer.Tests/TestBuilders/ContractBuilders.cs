using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.Tests.TestBuilders;

/// <summary>
/// Test-only builders to avoid brittle positional record constructor calls with named arguments.
/// Keep production contracts additive without triggering compile-error cascades in tests.
/// </summary>
public static class ContractBuilders
{
    public static ChildAgentHeartbeatRequest Heartbeat(
        string deviceName = "TestDevice",
        string agentVersion = "test",
        DateTimeOffset? sentAtUtc = null)
    {
        return new ChildAgentHeartbeatRequest(
            DeviceName: deviceName,
            AgentVersion: agentVersion,
            SentAtUtc: sentAtUtc ?? DateTimeOffset.UtcNow,
            EffectiveState: null,
            ScreenTime: null,
            Apps: null,
            Web: null,
            Circumvention: null,
            Tamper: null,
            LastAppliedPolicyVersion: null,
            LastAppliedPolicyEffectiveAtUtc: null,
            LastAppliedPolicyFingerprint: null,
            LastPolicyApplyFailedAtUtc: null,
            LastPolicyApplyError: null,
            PolicyApplyPendingSinceUtc: null,
            PolicyApplyOverdue: false,
            PolicyApplyState: null,
            LastKnownGoodPolicyVersion: null,
            RecommendedRollbackPolicyVersion: null,
            RecommendedRollbackReason: null,
            RecommendedRollbackGeneratedAtUtc: null
        );
    }
}
