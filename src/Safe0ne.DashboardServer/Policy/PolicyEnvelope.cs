using System.Text.Json;
using System.Text.Json.Serialization;

namespace Safe0ne.DashboardServer.Policy;

/// <summary>
/// Persisted policy snapshot.
///
/// Policy is stored as raw JSON to avoid coupling persistence to a particular PolicySet type.
/// </summary>
public sealed class PolicyEnvelope
{
    public int Version { get; init; } = 0;

    public DateTimeOffset LastUpdatedUtc { get; init; } = DateTimeOffset.UtcNow;

    public string UpdatedBy { get; init; } = "system";

    [JsonPropertyName("policy")]
    public JsonElement Policy { get; init; }

    public static PolicyEnvelope Create<T>(T policy, int version, string updatedBy)
    {
        var element = JsonSerializer.SerializeToElement(policy, JsonOptions.Default);
        return new PolicyEnvelope
        {
            Version = version,
            LastUpdatedUtc = DateTimeOffset.UtcNow,
            UpdatedBy = string.IsNullOrWhiteSpace(updatedBy) ? "unknown" : updatedBy,
            Policy = element
        };
    }

    public T? GetPolicy<T>()
    {
        if (Policy.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return default;

        return Policy.Deserialize<T>(JsonOptions.Default);
    }
}

internal static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
