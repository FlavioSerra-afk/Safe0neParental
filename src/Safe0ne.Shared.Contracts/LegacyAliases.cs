using System;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Back-compat alias for older tests/clients.
/// The canonical contract is <see cref="ChildAgentHeartbeatRequest"/>.
/// </summary>
public sealed record ChildHeartbeatRequest(
    string DeviceName,
    string AgentVersion,
    DateTimeOffset SentAtUtc,
    EffectiveChildState? EffectiveState = null,
    ScreenTimeReport? ScreenTime = null,
    AppUsageReport? Apps = null,
    WebReport? Web = null,
    CircumventionSignals? Circumvention = null,
    TamperSignals? Tamper = null,
    // 16W19: policy sync observability (agent -> SSOT ack)
    long? LastAppliedPolicyVersion = null,
    DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    string? LastAppliedPolicyFingerprint = null,
    // 16W19: best-effort apply failure signal
    DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    string? LastPolicyApplyError = null,
    // 16W21: watchdog / rollback hints for policy sync
    DateTimeOffset? PolicyApplyPendingSinceUtc = null,
    bool PolicyApplyOverdue = false,
    string? PolicyApplyState = null,
    long? LastKnownGoodPolicyVersion = null,
    // 16W23: server-side rollback recommendation (SSOT hints)
    long? RecommendedRollbackPolicyVersion = null,
    string? RecommendedRollbackReason = null,
    DateTimeOffset? RecommendedRollbackGeneratedAtUtc = null
);
