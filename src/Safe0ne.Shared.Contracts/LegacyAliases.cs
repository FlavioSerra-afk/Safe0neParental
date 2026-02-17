using System;
using System.Text.Json.Serialization;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Back-compat alias used by older tests/clients. Prefer <see cref="ChildAgentHeartbeatRequest"/> for new code.
/// </summary>
public sealed partial record ChildHeartbeatRequest(
    [property: JsonPropertyName("childId")] ChildId ChildId,
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("deviceName")] string? DeviceName = null,
    [property: JsonPropertyName("agentVersion")] string? AgentVersion = null,
    [property: JsonPropertyName("osVersion")] string? OsVersion = null,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset? SentAtUtc = null,
    // Policy apply observability (optional)
    [property: JsonPropertyName("lastAppliedPolicyVersion")] long? LastAppliedPolicyVersion = null,
    [property: JsonPropertyName("lastAppliedPolicyEffectiveAtUtc")] DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyFailedAtUtc")] DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyError")] string? LastPolicyApplyError = null
);
