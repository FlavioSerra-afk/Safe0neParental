using System;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Back-compat overload: some older tests/clients construct ChildHeartbeatRequest with a DeviceId named argument.
/// We keep the SSOT authoritative device identity elsewhere; this overload ignores DeviceId but preserves compile-time
/// compatibility for named-argument construction.
/// </summary>
public sealed partial record ChildHeartbeatRequest
{
    // NOTE: This must NOT introduce another primary-constructor parameter list (CS8863).
    // It is an additional ctor overload that forwards to the existing primary ctor using named args.
    public ChildHeartbeatRequest(
        ChildId ChildId,
        Guid DeviceId,
        string DeviceName,
        string AgentVersion,
        DateTimeOffset SentAtUtc,
        EffectiveChildState? EffectiveState = null,
        ScreenTimeReport? ScreenTime = null,
        AppUsageReport? Apps = null,
        WebReport? Web = null,
        CircumventionSignals? Circumvention = null,
        TamperSignals? Tamper = null,
        long? LastAppliedPolicyVersion = null,
        DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
        string? LastAppliedPolicyFingerprint = null,
        DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
        string? LastPolicyApplyError = null,
        DateTimeOffset? PolicyApplyPendingSinceUtc = null,
        bool PolicyApplyOverdue = false,
        string? PolicyApplyState = null,
        long? LastKnownGoodPolicyVersion = null,
        long? RecommendedRollbackPolicyVersion = null,
        string? RecommendedRollbackReason = null,
        DateTimeOffset? RecommendedRollbackGeneratedAtUtc = null
    )
    : this(
        ChildId: ChildId,
        DeviceName: DeviceName,
        AgentVersion: AgentVersion,
        SentAtUtc: SentAtUtc,
        EffectiveState: EffectiveState,
        ScreenTime: ScreenTime,
        Apps: Apps,
        Web: Web,
        Circumvention: Circumvention,
        Tamper: Tamper,
        LastAppliedPolicyVersion: LastAppliedPolicyVersion,
        LastAppliedPolicyEffectiveAtUtc: LastAppliedPolicyEffectiveAtUtc,
        LastAppliedPolicyFingerprint: LastAppliedPolicyFingerprint,
        LastPolicyApplyFailedAtUtc: LastPolicyApplyFailedAtUtc,
        LastPolicyApplyError: LastPolicyApplyError,
        PolicyApplyPendingSinceUtc: PolicyApplyPendingSinceUtc,
        PolicyApplyOverdue: PolicyApplyOverdue,
        PolicyApplyState: PolicyApplyState,
        LastKnownGoodPolicyVersion: LastKnownGoodPolicyVersion,
        RecommendedRollbackPolicyVersion: RecommendedRollbackPolicyVersion,
        RecommendedRollbackReason: RecommendedRollbackReason,
        RecommendedRollbackGeneratedAtUtc: RecommendedRollbackGeneratedAtUtc
    )
    {
        // Intentionally ignored: DeviceId is not required for control-plane heartbeat contract compatibility.
        _ = DeviceId;
    }
}
