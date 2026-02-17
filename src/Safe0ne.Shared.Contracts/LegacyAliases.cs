using System.Text.Json.Serialization;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Back-compat DTO for older tests/clients that still reference <c>ChildHeartbeatRequest</c>.
/// Prefer <see cref="ChildAgentHeartbeatRequest"/> for new code.
/// </summary>
/// <remarks>
/// Keep this type small and stable. Remove only as part of an explicit migration patch once all call-sites are upgraded.
/// </remarks>
public sealed partial record ChildHeartbeatRequest(
    [property: JsonPropertyName("childId")] ChildId ChildId,
    [property: JsonPropertyName("deviceId")] Guid DeviceId,
    [property: JsonPropertyName("deviceName")] string DeviceName,
    [property: JsonPropertyName("agentVersion")] string AgentVersion,
    [property: JsonPropertyName("osVersion")] string? OsVersion = null,
    [property: JsonPropertyName("appVersion")] string? AppVersion = null,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc = default,
    [property: JsonPropertyName("effectiveState")] EffectiveChildState? EffectiveState = null,
    [property: JsonPropertyName("screenTime")] ScreenTimeReport? ScreenTime = null,
    [property: JsonPropertyName("apps")] AppUsageReport? Apps = null,
    [property: JsonPropertyName("web")] WebReport? Web = null,
    [property: JsonPropertyName("circumvention")] CircumventionSignals? Circumvention = null,
    [property: JsonPropertyName("tamper")] TamperSignals? Tamper = null,
    // Policy apply observability (optional; safe to omit)
    [property: JsonPropertyName("lastAppliedPolicyVersion")] long? LastAppliedPolicyVersion = null,
    [property: JsonPropertyName("lastAppliedPolicyAtUtc")] DateTimeOffset? LastAppliedPolicyAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyFailedAtUtc")] DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyError")] string? LastPolicyApplyError = null
);
