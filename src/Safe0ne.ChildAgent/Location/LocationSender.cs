using System.Net;
using System.Net.Http.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Location;

/// <summary>
/// Local Mode location scaffold.
///
/// Today: posts either "unavailable" or a test/fake coordinate.
/// Later: can be replaced with real Windows location capture (with explicit user permission).
/// </summary>
public sealed class LocationSender
{
    /// <summary>
    /// Optional dev-only override for location testing.
    /// Format: "lat,lon" or "lat,lon,accuracyMeters".
    /// </summary>
    private const string FakeLocationEnv = "SAFEONE_FAKE_LOCATION";

    public async Task PostLastKnownAsync(HttpClient client, ChildId childId, string deviceName, DateTimeOffset capturedAtUtc, CancellationToken ct)
    {
        var payload = BuildPayload(deviceName, capturedAtUtc);

        // Local Mode first.
        try
        {
            using var r = await client.PostAsJsonAsync($"/api/local/children/{childId.Value}/location", payload, JsonDefaults.Options, ct);
            if (r.IsSuccessStatusCode)
            {
                return;
            }

            // If Local API isn't present in this build, fall back to v1 (if/when implemented).
            if (r.StatusCode != HttpStatusCode.NotFound)
            {
                return;
            }
        }
        catch
        {
            // Best-effort: never crash the agent because of location.
            return;
        }

        // Legacy fallback (may not exist yet; safe no-op).
        try
        {
            using var _ = await client.PostAsJsonAsync($"/api/{ApiVersions.V1}/children/{childId.Value}/location", payload, JsonDefaults.Options, ct);
        }
        catch
        {
            // ignore
        }
    }

    private static object BuildPayload(string deviceName, DateTimeOffset capturedAtUtc)
    {
        var fake = Environment.GetEnvironmentVariable(FakeLocationEnv);
        if (!string.IsNullOrWhiteSpace(fake) && TryParseFake(fake, out var lat, out var lon, out var acc))
        {
            return new
            {
                available = true,
                capturedAtUtc = capturedAtUtc,
                latitude = lat,
                longitude = lon,
                accuracyMeters = acc,
                source = "kid_fake",
                deviceId = deviceName,
                note = $"Provided via {FakeLocationEnv}"
            };
        }

        return new
        {
            available = false,
            capturedAtUtc = capturedAtUtc,
            source = "kid_unavailable",
            deviceId = deviceName,
            note = "Location capture not implemented (scaffold)."
        };
    }

    private static bool TryParseFake(string value, out double lat, out double lon, out double? acc)
    {
        lat = 0;
        lon = 0;
        acc = null;

        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2 || parts.Length > 3)
        {
            return false;
        }

        if (!double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lat))
        {
            return false;
        }
        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out lon))
        {
            return false;
        }
        if (parts.Length == 3)
        {
            if (double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                acc = a;
            }
        }

        return true;
    }
}
