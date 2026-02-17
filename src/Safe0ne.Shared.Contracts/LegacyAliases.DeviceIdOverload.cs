namespace Safe0ne.Shared.Contracts;

// Back-compat constructor overloads for legacy tests/client code that use named args:
//   new ChildHeartbeatRequest(ChildId: ..., DeviceId: ...)
//
// NOTE: ChildId is a value object (record struct ChildId(Guid Value)).
// The primary record constructor for ChildHeartbeatRequest uses Guid for ChildId.
// So we must pass childId.Value when chaining.

public sealed partial record ChildHeartbeatRequest
{
    /// <summary>
    /// Back-compat overload: accepts <see cref="ChildId"/> and <see cref="Guid"/> DeviceId.
    /// </summary>
    public ChildHeartbeatRequest(
        ChildId childId,
        Guid deviceId,
        string? deviceName = null,
        string? agentVersion = null,
        DateTimeOffset? sentAtUtc = null,
        long? lastAppliedPolicyVersion = null,
        DateTimeOffset? lastAppliedPolicyEffectiveAtUtc = null,
        DateTimeOffset? lastPolicyApplyFailedAtUtc = null,
        string? lastPolicyApplyError = null)
        : this(
            ChildId: childId.Value,
            DeviceId: deviceId,
            DeviceName: deviceName,
            AgentVersion: agentVersion,
            SentAtUtc: sentAtUtc,
            LastAppliedPolicyVersion: lastAppliedPolicyVersion,
            LastAppliedPolicyEffectiveAtUtc: lastAppliedPolicyEffectiveAtUtc,
            LastPolicyApplyFailedAtUtc: lastPolicyApplyFailedAtUtc,
            LastPolicyApplyError: lastPolicyApplyError)
    {
    }
}
