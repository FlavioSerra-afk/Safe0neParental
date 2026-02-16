namespace Safe0ne.Shared.Contracts;

/// <summary>
/// K11: Mobile-only stubs.
///
/// These models exist so future iOS/Android features (location, geofences, SOS) can be
/// introduced without redesigning core policy/contract shapes.
///
/// Windows-first prototype does NOT implement location tracking.
/// </summary>
public sealed record GeoCoordinate(
    double Latitude,
    double Longitude,
    double? AccuracyMeters = null);

public enum GeofenceTrigger
{
    Enter = 0,
    Exit = 1,
    EnterOrExit = 2
}

public enum GeofenceAction
{
    Alert = 0,
    Block = 1
}

/// <summary>
/// Simple circular geofence rule.
///
/// Mobile-only: used for future location alerts and geofence-based restrictions.
/// </summary>
public sealed record GeofenceRule(
    string Id,
    string Name,
    GeoCoordinate Center,
    int RadiusMeters,
    GeofenceTrigger Trigger = GeofenceTrigger.Exit,
    GeofenceAction Action = GeofenceAction.Alert,
    bool Enabled = true);

/// <summary>
/// Future SOS signal from a child device.
/// Windows-first prototype does not produce these signals.
/// </summary>
public sealed record SosSignal(
    ChildId ChildId,
    DateTimeOffset RaisedAtUtc,
    string? Message = null,
    GeoCoordinate? Location = null);
