using System.Globalization;
using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Location;

/// <summary>
/// Kid-side geofence evaluation.
///
/// Current design (PATCH 16U14):
/// - Uses policy surface: policy.location.geofences[] (id, name, latitude, longitude, radiusMeters, mode).
/// - Uses a test/fake coordinate from SAFEONE_FAKE_LOCATION (same env as LocationSender) until real capture exists.
/// - Keeps derived inside/outside state in-memory for the current agent session.
/// - Emits Activity events: geofence_enter / geofence_exit.
///
/// Later: persist derived state into SSOT locationState.geofences once a dedicated Local API exists.
/// </summary>
public sealed class GeofenceEvaluator
{
    private const string FakeLocationEnv = "SAFEONE_FAKE_LOCATION";

    public readonly record struct Transition(string Kind, string DetailsJson);

    public sealed record RuntimeState(bool Inside, DateTimeOffset LastTransitionAtUtc);

    public static bool TryGetCurrentLocation(out double lat, out double lon, out double? accuracyMeters)
    {
        lat = 0;
        lon = 0;
        accuracyMeters = null;

        var fake = Environment.GetEnvironmentVariable(FakeLocationEnv);
        if (string.IsNullOrWhiteSpace(fake)) return false;

        var parts = fake.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3) return false;

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat)) return false;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lon)) return false;
        if (parts.Length == 3 && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var acc))
        {
            accuracyMeters = acc;
        }

        return true;
    }

    public IEnumerable<Transition> Evaluate(
        JsonElement policySurface,
        double lat,
        double lon,
        double? accuracyMeters,
        DateTimeOffset nowUtc,
        Dictionary<string, RuntimeState> runtime)
    {
        if (!TryGetGeofenceArray(policySurface, out var geofences))
        {
            yield break;
        }

        // Remove runtime entries for geofences that no longer exist.
        var liveIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in geofences.EnumerateArray())
        {
            var id = GetString(g, "id");
            if (!string.IsNullOrWhiteSpace(id)) liveIds.Add(id);
        }
        if (runtime.Count > 0)
        {
            var stale = runtime.Keys.Where(k => !liveIds.Contains(k)).ToList();
            foreach (var k in stale) runtime.Remove(k);
        }

        foreach (var g in geofences.EnumerateArray())
        {
            var id = GetString(g, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var name = GetString(g, "name") ?? "Geofence";

            // Support both latitude/longitude and legacy lat/lon.
            if (!TryGetNumber(g, "latitude", out var glat) && !TryGetNumber(g, "lat", out glat)) continue;
            if (!TryGetNumber(g, "longitude", out var glon) && !TryGetNumber(g, "lon", out glon)) continue;
            if (!TryGetNumber(g, "radiusMeters", out var radius) && !TryGetNumber(g, "radius", out radius)) radius = 200;

            var mode = (GetString(g, "mode") ?? "inside").ToLowerInvariant();
            if (mode != "outside") mode = "inside";

            var distMeters = HaversineMeters(lat, lon, glat, glon);
            var inside = distMeters <= Math.Max(10, radius);

            if (!runtime.TryGetValue(id, out var st))
            {
                // Prime the state without emitting.
                runtime[id] = new RuntimeState(inside, DateTimeOffset.MinValue);
                continue;
            }

            if (st.Inside == inside) continue;

            // Debounce: avoid rapid oscillation (GPS jitter). Keep it simple.
            if (st.LastTransitionAtUtc != DateTimeOffset.MinValue && (nowUtc - st.LastTransitionAtUtc) < TimeSpan.FromSeconds(20))
            {
                continue;
            }

            runtime[id] = new RuntimeState(inside, nowUtc);

            var kind = inside ? "geofence_enter" : "geofence_exit";
            var details = JsonSerializer.Serialize(new
            {
                geofenceId = id,
                name,
                mode,
                center = new { latitude = glat, longitude = glon },
                radiusMeters = radius,
                deviceLocation = new { latitude = lat, longitude = lon, accuracyMeters },
                distanceMeters = Math.Round(distMeters, 1),
                inside
            }, JsonDefaults.Options);

            yield return new Transition(kind, details);
        }
    }

    private static bool TryGetGeofenceArray(JsonElement policySurface, out JsonElement geofences)
    {
        geofences = default;
        if (policySurface.ValueKind != JsonValueKind.Object) return false;
        if (!policySurface.TryGetProperty("location", out var loc) || loc.ValueKind != JsonValueKind.Object) return false;
        if (!loc.TryGetProperty("geofences", out geofences) || geofences.ValueKind != JsonValueKind.Array) return false;
        return true;
    }

    private static string? GetString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryGetNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        if (!obj.TryGetProperty(name, out var v)) return false;
        if (v.ValueKind == JsonValueKind.Number) return v.TryGetDouble(out value);
        return false;
    }

    // Haversine distance in meters.
    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000; // meters
        var phi1 = DegreesToRadians(lat1);
        var phi2 = DegreesToRadians(lat2);
        var dPhi = DegreesToRadians(lat2 - lat1);
        var dLam = DegreesToRadians(lon2 - lon1);

        var a = Math.Sin(dPhi / 2) * Math.Sin(dPhi / 2)
              + Math.Cos(phi1) * Math.Cos(phi2)
              * Math.Sin(dLam / 2) * Math.Sin(dLam / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double DegreesToRadians(double deg) => deg * (Math.PI / 180.0);
}
