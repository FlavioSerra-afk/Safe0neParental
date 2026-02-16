namespace Safe0ne.Shared.Contracts;

public enum SafetyMode
{
    Open = 0,
    Homework = 1,
    Bedtime = 2,
    Lockdown = 3
}

public sealed record ChildProfile(
    ChildId Id,
    string DisplayName);

/// <summary>
/// A daily time window expressed in local time (HH:mm).
/// Windows that cross midnight are supported (e.g. 21:00 → 07:00).
///
/// NOTE: Windows-first prototype assumes Parent + Child share the same local timezone.
/// </summary>
public sealed record ScheduleWindow(
    bool Enabled = false,
    string StartLocal = "22:00",
    string EndLocal = "07:00");

/// <summary>
/// Best-effort per-app daily limit (Windows-first: process name / executable).
/// Example ProcessName: "chrome.exe"
/// </summary>
public sealed record PerAppLimit(
    string ProcessName,
    int LimitMinutes);

/// <summary>
/// Coarse web categories used for filtering decisions (v1).
/// Unknown = not classified by the on-device category engine.
/// </summary>
public enum WebCategory
{
    Unknown = 0,
    Adult = 1,
    Social = 2,
    Games = 3,
    Streaming = 4,
    Shopping = 5
}

/// <summary>
/// Category rule action.
/// Allow = permitted.
/// Alert = permitted but reported.
/// Block = restricted (best effort).
/// </summary>
public enum WebRuleAction
{
    Allow = 0,
    Alert = 1,
    Block = 2
}

public sealed record WebCategoryRule(
    WebCategory Category,
    WebRuleAction Action);

/// <summary>
/// Child policy snapshot (v1).
///
/// NOTE: New fields MUST be appended with defaults so older JSON can still deserialize
/// (missing JSON members map to optional constructor parameters).
/// </summary>
public sealed record ChildPolicy(
    ChildId ChildId,
    PolicyVersion Version,
    SafetyMode Mode,
    DateTimeOffset UpdatedAtUtc,
    string UpdatedBy,
    DateTimeOffset? GrantUntilUtc = null,
    bool AlwaysAllowed = false,
    // K3/K5: deny list (Windows-first prototype uses process executable names)
    string[]? BlockedProcessNames = null,
    // K4/P6: Screen time policies
    int? DailyScreenTimeLimitMinutes = null,
    ScheduleWindow? BedtimeWindow = null,
    ScheduleWindow? SchoolWindow = null,
    ScheduleWindow? HomeworkWindow = null,
    // K5/P7: Apps & games controls (Windows-first, best-effort)
    bool AppsAllowListEnabled = false,
    string[]? AllowedProcessNames = null,
    PerAppLimit[]? PerAppDailyLimits = null,
    // K6/P8: Web & content filtering (Windows-first, best effort)
    bool WebAdultBlockEnabled = false,
    WebCategoryRule[]? WebCategoryRules = null,
    string[]? WebAllowedDomains = null,
    string[]? WebBlockedDomains = null,
    bool WebCircumventionDetectionEnabled = true,
    bool WebSafeSearchEnabled = false,
    // K11: Mobile-only stubs (future)
    bool LocationSharingEnabled = false,
    GeofenceRule[]? Geofences = null,
    bool SosEnabled = false);

public sealed record EffectiveChildState(
    ChildId ChildId,
    PolicyVersion PolicyVersion,
    SafetyMode ConfiguredMode,
    SafetyMode EffectiveMode,
    string ReasonCode,
    DateTimeOffset EvaluatedAtUtc,
    DateTimeOffset? GrantUntilUtc,
    bool AlwaysAllowed,
    // Optional: which schedule window is currently driving the result (if any)
    string? ActiveSchedule = null,
    // K8/P11: active time-boxed grants (additive)
    Grant[]? ActiveGrants = null);
