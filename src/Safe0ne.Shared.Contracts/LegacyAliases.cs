using System;
using System.Text.Json.Serialization;
// LEGACY-COMPAT: contract alias shims for migration | remove when all callers use canonical contract shapes

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Back-compat DTO(s) kept to avoid churn in older tests/tools.
/// NOTE: SSOT uses the canonical DTOs (e.g., ChildAgentHeartbeatRequest). This is only an alias surface.
/// </summary>
public static class LegacyAliases
{
    // Intentionally empty: serves as an anchor for the file and future aliases.
}

/// <summary>
/// Legacy name used by older tests. Keep constructor parameters aligned to named-arg usage in tests.
/// </summary>
public sealed record ChildHeartbeatRequest(
    [property: JsonPropertyName("childId")] ChildId ChildId,
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("deviceName")] string? DeviceName = null,
    [property: JsonPropertyName("agentVersion")] string? AgentVersion = null,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset? SentAtUtc = null,
    [property: JsonPropertyName("lastAppliedPolicyVersion")] long? LastAppliedPolicyVersion = null,
    [property: JsonPropertyName("lastAppliedPolicyEffectiveAtUtc")] DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyFailedAtUtc")] DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyError")] string? LastPolicyApplyError = null
);
