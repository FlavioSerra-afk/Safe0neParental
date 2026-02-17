using System.Text.Json.Serialization;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Back-compat DTO used by older tests/clients. The canonical payload is the anonymous JSON object
/// sent to /api/v1/children/{childId}/heartbeat, but some code paths referenced this type name.
/// </summary>
public sealed record ChildHeartbeatRequest(
    [property: JsonPropertyName("childId")] Guid ChildId,
    [property: JsonPropertyName("deviceName")] string DeviceName,
    [property: JsonPropertyName("agentVersion")] string AgentVersion,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc,
    [property: JsonPropertyName("lastAppliedPolicyVersion")] long? LastAppliedPolicyVersion = null,
    [property: JsonPropertyName("lastAppliedPolicyEffectiveAtUtc")] DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyFailedAtUtc")] DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyError")] string? LastPolicyApplyError = null
);
