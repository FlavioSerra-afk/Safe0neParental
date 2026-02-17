using System.Text.Json.Serialization;

namespace Safe0ne.Shared.Contracts;

public static class ApiVersions
{
    public const string V1 = "v1";
}

/// <summary>
/// Shared constants for agent authentication.
/// </summary>
public static class AgentAuth
{
    /// <summary>
    /// Header that carries the paired device token.
    /// </summary>
    public const string DeviceTokenHeaderName = "X-Safe0ne-Device-Token";
}

public sealed record ApiError(
    string Code,
    string Message,
    string? Detail = null);

public sealed record ApiResponse<T>(
    [property: JsonPropertyName("data")] T? Data,
    [property: JsonPropertyName("error")] ApiError? Error)
{
    [JsonIgnore]
    public bool Ok => Error is null;
}

/// <summary>
/// Update request for a child policy. Optional fields are patch-like: null means "no change".
/// </summary>
public sealed record UpdateChildPolicyRequest(
    SafetyMode Mode,
    string UpdatedBy,
    bool? AlwaysAllowed = null,
    int? GrantMinutes = null,
    string[]? BlockedProcessNames = null,
    // K4/P6: screen time + schedules (patch-like semantics)
    int? DailyScreenTimeLimitMinutes = null,
    ScheduleWindow? BedtimeWindow = null,
    ScheduleWindow? SchoolWindow = null,
    ScheduleWindow? HomeworkWindow = null,
    // K5/P7: apps & games
    bool? AppsAllowListEnabled = null,
    string[]? AllowedProcessNames = null,
    PerAppLimit[]? PerAppDailyLimits = null,
    // K6/P8: web & content filtering
    bool? WebAdultBlockEnabled = null,
    WebCategoryRule[]? WebCategoryRules = null,
    string[]? WebAllowedDomains = null,
    string[]? WebBlockedDomains = null,
    bool? WebCircumventionDetectionEnabled = null,
    bool? WebSafeSearchEnabled = null,
    // K10+: device integrity (tamper/circumvention) signals + alert routing gates
    bool? DeviceTamperDetectionEnabled = null,
    bool? DeviceTamperAlertsEnabled = null,
    bool? WebCircumventionAlertsEnabled = null);

public sealed record ScreenTimeReport(
    // Local date for “today” on the child device (yyyy-MM-dd)
    string LocalDate,
    int UsedSecondsToday,
    int? DailyLimitMinutes = null,
    bool BudgetDepleted = false);

// K5: privacy-first app usage aggregates (Windows-first; best-effort)
public sealed record AppUsageItem(
    string ProcessName,
    int UsedSecondsToday);

public sealed record BlockedAttemptItem(
    string ProcessName,
    int Count,
    string Reason);

public sealed record AppUsageReport(
    // Local date for “today” on the child device (yyyy-MM-dd)
    string LocalDate,
    AppUsageItem[]? Usage = null,
    BlockedAttemptItem[]? BlockedAttempts = null);

// K6: Web filtering aggregates (privacy-first)
public sealed record WebBlockedDomainItem(
    string Domain,
    int Count,
    string Reason);

public sealed record WebReport(
    // Local date for “today” on the child device (yyyy-MM-dd)
    string LocalDate,
    int BlockedDomainsConfigured = 0,
    WebBlockedDomainItem[]? TopBlockedDomains = null,
    int AlertsToday = 0);

public sealed record CircumventionSignals(
    bool VpnSuspected = false,
    bool ProxyEnabled = false,
    bool PublicDnsDetected = false,
    bool HostsWriteFailed = false,
    string[]? Notes = null);

/// <summary>
/// K10: Tamper / health signals from the agent (privacy-first).
/// These are coarse booleans and optional notes to support parent troubleshooting.
/// NOTE: New fields must be appended with defaults for backward-compatible JSON.
/// </summary>
public sealed record TamperSignals(
    bool NotRunningElevated = false,
    bool EnforcementError = false,
    string? LastError = null,
    DateTimeOffset? LastErrorAtUtc = null,
    string[]? Notes = null);

/// <summary>
/// Child Agent -> Control Plane heartbeat.
/// NOTE: New fields must be appended with defaults for backward-compatible JSON.
/// </summary>
public sealed record ChildAgentHeartbeatRequest(
    string DeviceName,
    string AgentVersion,
    DateTimeOffset SentAtUtc,
    EffectiveChildState? EffectiveState = null,
    // K4: aggregated daily usage (privacy-first)
    ScreenTimeReport? ScreenTime = null,
    // K5: app usage + blocked attempts (privacy-first)
    AppUsageReport? Apps = null,
    // K6: web filtering aggregates + circumvention signals
    WebReport? Web = null,
    CircumventionSignals? Circumvention = null,
    TamperSignals? Tamper = null,
    // 16W19: policy sync observability (agent -> SSOT ack)
    long? LastAppliedPolicyVersion = null,
    DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    string? LastAppliedPolicyFingerprint = null,
    // 16W19: best-effort apply failure signal (activity-backed; persisted as status fields too)
    DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    string? LastPolicyApplyError = null,
    // 16W21: watchdog / rollback hints for policy sync
    DateTimeOffset? PolicyApplyPendingSinceUtc = null,
    bool PolicyApplyOverdue = false,
    string? PolicyApplyState = null,
    long? LastKnownGoodPolicyVersion = null
);

/// <summary>
/// Diagnostics bundle metadata for support exports.
/// NOTE: New fields must be appended with defaults for backward-compatible JSON.
/// </summary>
public sealed record DiagnosticsBundleInfo(
    ChildId ChildId,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes,
    string FileName);


/// <summary>
/// Agent status as last seen by the control plane.
/// NOTE: New fields must be appended with defaults for backward-compatible JSON.
/// </summary>
public sealed record ChildAgentStatus(
    ChildId ChildId,
    DateTimeOffset LastSeenUtc,
    string DeviceName,
    string AgentVersion,
    SafetyMode EffectiveMode,
    string ReasonCode,
    PolicyVersion PolicyVersion,
    bool Authenticated = false,
    Guid? DeviceId = null,
    // K4: screen time rollup shown in parent UI
    int? ScreenTimeLimitMinutes = null,
    int? ScreenTimeUsedMinutes = null,
    int? ScreenTimeRemainingMinutes = null,
    bool ScreenTimeBudgetDepleted = false,
    string? ActiveSchedule = null,
    // K5: blocked attempts + app usage rollups
    int BlockedAttemptsToday = 0,
    BlockedAttemptItem[]? TopBlockedApps = null,
    AppUsageItem[]? TopAppUsage = null,
    // K6: web & circumvention rollups
    int WebBlockedDomainsConfigured = 0,
    WebBlockedDomainItem[]? WebTopBlockedDomains = null,
    int WebAlertsToday = 0,
    CircumventionSignals? Circumvention = null,
    TamperSignals? Tamper = null,
    // 16W19: policy sync observability (what the agent actually applied)
    long? LastAppliedPolicyVersion = null,
    DateTimeOffset? LastAppliedPolicyEffectiveAtUtc = null,
    string? LastAppliedPolicyFingerprint = null,
    DateTimeOffset? LastPolicyApplyFailedAtUtc = null,
    string? LastPolicyApplyError = null,
    // 16W21: watchdog / rollback hints for policy sync
    DateTimeOffset? PolicyApplyPendingSinceUtc = null,
    bool PolicyApplyOverdue = false,
    string? PolicyApplyState = null,
    long? LastKnownGoodPolicyVersion = null
);

/// <summary>
/// Response returned when a parent generates a pairing code for a child device.
/// </summary>
public sealed record PairingStartResponse(
    ChildId ChildId,
    string PairingCode,
    DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Request sent by ChildAgent to claim a pairing code.
/// </summary>
public sealed record PairingCompleteRequest(
    string PairingCode,
    string DeviceName,
    string AgentVersion);

/// <summary>
/// Response returned to ChildAgent after successful pairing.
/// </summary>
public sealed record PairingCompleteResponse(
    ChildId ChildId,
    Guid DeviceId,
    string DeviceToken,
    DateTimeOffset IssuedAtUtc);

public sealed record ChildDeviceSummary(
    Guid DeviceId,
    string DeviceName,
    string AgentVersion,
    DateTimeOffset PairedAtUtc,
    DateTimeOffset? LastSeenUtc = null,
    // Pairing hardening: token metadata for observability and revoke/expiry.
    DateTimeOffset? TokenIssuedAtUtc = null,
    DateTimeOffset? TokenExpiresAtUtc = null,
    DateTimeOffset? TokenRevokedAtUtc = null,
    string? TokenRevokedBy = null,
    bool TokenExpired = false,
    bool TokenRevoked = false);

/// <summary>
/// Parent -> Control Plane: revoke a device token without deleting the device record.
/// </summary>
public sealed record RevokeDeviceTokenRequest(
    string? RevokedBy = null,
    string? Reason = null);

/// <summary>
/// Command types for Control Plane -> Child Agent messages.
/// Keep as strings for forward compatibility.
/// </summary>
public static class CommandTypes
{
    public const string Notice = "notice";
    public const string Sync = "sync";
    public const string Ping = "ping";
    public const string DiagnosticsBundle = "diagnostics_bundle";
}

/// <summary>
/// Parent -> Control Plane: create a command for a child device.
/// </summary>
public sealed record CreateChildCommandRequest(
    string Type,
    string? PayloadJson = null,
    int? ExpiresInMinutes = null);

/// <summary>
/// Control Plane -> Agent command envelope (persisted).
/// NOTE: New fields must be appended with defaults for backward-compatible JSON.
/// </summary>
public sealed record ChildCommand(
    Guid CommandId,
    string Type,
    DateTimeOffset CreatedAtUtc,
    string? PayloadJson = null,
    DateTimeOffset? ExpiresAtUtc = null,
    bool Acked = false,
    DateTimeOffset? AckedAtUtc = null,
    Guid? AckedByDeviceId = null,
    string? AckResult = null,
    string? AckDetail = null);

/// <summary>
/// Agent -> Control Plane: acknowledge a command.
/// </summary>
public sealed record AckChildCommandRequest(
    string Result,
    string? Detail = null);
