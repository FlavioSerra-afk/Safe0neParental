using System;

namespace Safe0ne.Shared.Contracts;

// Back-compat ctor overload: allows older call sites/tests to use named argument `UtcNow:`
// without changing the primary record signature.
//
// NOTE: Requires the primary declaration of ChildHeartbeatRequest to be `partial`.
public sealed partial record ChildHeartbeatRequest
{
    // Named-arg friendly overload (UtcNow) -> chains to primary ctor's SentAtUtc parameter.
    public ChildHeartbeatRequest(
        ChildId childId,
        Guid deviceId,
        string? deviceName = null,
        string? agentVersion = null,
        string? osVersion = null,
        string? appVersion = null,
        DateTimeOffset? utcNow = null,
        long? lastAppliedPolicyVersion = null,
        DateTimeOffset? lastAppliedPolicyEffectiveAtUtc = null,
        DateTimeOffset? lastPolicyApplyFailedAtUtc = null,
        string? lastPolicyApplyError = null,
        bool _utcNowAlias = true)
        : this(
            childId,
            deviceId,
            deviceName,
            agentVersion,
            osVersion,
            appVersion,
            utcNow ?? DateTimeOffset.UtcNow,
            lastAppliedPolicyVersion,
            lastAppliedPolicyEffectiveAtUtc,
            lastPolicyApplyFailedAtUtc,
            lastPolicyApplyError)
    {
    }
}
