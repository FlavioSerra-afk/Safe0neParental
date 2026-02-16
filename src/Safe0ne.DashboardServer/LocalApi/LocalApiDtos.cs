using System.Text.Json;

namespace Safe0ne.DashboardServer.LocalApi;

// Child metadata that the Parent UI needs immediately (name, gender, age group, avatar).
public sealed record LocalAvatarCrop(double X, double Y, double Zoom);
public sealed record LocalAvatar(string Kind, int? Id = null, string? DataUrl = null, LocalAvatarCrop? Crop = null);

public sealed record CreateLocalChildRequest(
    string DisplayName,
    string? Gender = null,
    string? AgeGroup = null,
    LocalAvatar? Avatar = null);

public sealed record PatchLocalChildRequest(
    string? DisplayName = null,
    bool? Archived = null,
    string? Gender = null,
    string? AgeGroup = null,
    LocalAvatar? Avatar = null);

// Settings/profile blob used by the Parent UI Settings tab.
// Keep this flexible: the SSOT stores it as JSON, and the API transmits it as JSON.
public sealed record LocalChildSettingsProfile(JsonElement Value);

// Activity events sent from Kid -> Parent via local SSOT.
public sealed record LocalActivityEvent(
    Guid EventId,
    DateTimeOffset OccurredAtUtc,
    string Kind,
    string? App = null,
    string? Details = null,
    string? Severity = null,
    string? DeviceId = null);

public sealed record PostLocalActivityBatchRequest(List<LocalActivityEvent> Events);



// Location (scaffold): kid posts last known location; server stores last known only.
public sealed record LocalChildLocation(
    bool Available,
    DateTimeOffset? CapturedAtUtc = null,
    double? Latitude = null,
    double? Longitude = null,
    double? AccuracyMeters = null,
    string? Source = null,
    string? Note = null);

public sealed record PostLocalLocationRequest(LocalChildLocation Location);
