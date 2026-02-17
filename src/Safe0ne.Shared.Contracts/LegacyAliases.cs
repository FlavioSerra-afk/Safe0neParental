using System;
using System.Text.Json.Serialization;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Back-compat DTO used by older tests/clients.
/// 
/// NOTE: The canonical heartbeat payload is posted to:
///   /api/v1/children/{childId}/heartbeat
/// and the canonical contract type is <see cref="ChildAgentHeartbeatRequest"/>.
/// Some older tests referenced <c>ChildHeartbeatRequest</c> by name and also used named
/// constructor arguments (e.g. <c>ChildId:</c>). This DTO exists solely to keep those
/// tests compiling.
/// </summary>
public sealed partial record ChildHeartbeatRequest(
    [property: JsonPropertyName("childId")] Guid ChildId,
    [property: JsonPropertyName("deviceName")] string DeviceName,
    [property: JsonPropertyName("agentVersion")] string AgentVersion,
    [property: JsonPropertyName("sentAtUtc")] DateTimeOffset SentAtUtc,
    [property: JsonPropertyName("effectiveState")] EffectiveChildState? EffectiveState = null,
    [property: JsonPropertyName("screenTime")] ScreenTimeReport? ScreenTime = null,
    [property: JsonPropertyName("apps")] AppUsageReport? Apps = null,
    [property: JsonPropertyName("web")] WebReport? Web = null,
    [property: JsonPropertyName("circumvention")] CircumventionSignals? Circumvention = null,
    [property: JsonPropertyName("tamper")] TamperSignals? Tamper = null,
    // 16W19: policy sync observability (agent -> SSOT ack)
    [property: JsonPropertyName("lastAppliedPolicyVersion")] long? LastAppliedPolicyVersion = null,
    [property: JsonPropertyName("lastAppliedPolicyEffectiveAtUtc")] DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    [property: JsonPropertyName("lastAppliedPolicyFingerprint")] string? LastAppliedPolicyFingerprint = null,
    // 16W19: best-effort apply failure signal
    [property: JsonPropertyName("lastPolicyApplyFailedAtUtc")] DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    [property: JsonPropertyName("lastPolicyApplyError")] string? LastPolicyApplyError = null,
    // 16W21: watchdog / rollback hints for policy sync
    [property: JsonPropertyName("policyApplyPendingSinceUtc")] DateTimeOffset? PolicyApplyPendingSinceUtc = null,
    [property: JsonPropertyName("policyApplyOverdue")] bool PolicyApplyOverdue = false,
    [property: JsonPropertyName("policyApplyState")] string? PolicyApplyState = null,
    [property: JsonPropertyName("lastKnownGoodPolicyVersion")] long? LastKnownGoodPolicyVersion = null,
    // 16W23: server-side rollback recommendation (SSOT hints)
    [property: JsonPropertyName("recommendedRollbackPolicyVersion")] long? RecommendedRollbackPolicyVersion = null,
    [property: JsonPropertyName("recommendedRollbackReason")] string? RecommendedRollbackReason = null,
    [property: JsonPropertyName("recommendedRollbackGeneratedAtUtc")] DateTimeOffset? RecommendedRollbackGeneratedAtUtc = null
);
