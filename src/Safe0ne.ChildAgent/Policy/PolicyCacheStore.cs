using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Policy;

/// <summary>
/// Offline-first policy cache (best-effort).
/// Stores the last known policy surface + mapped v1 policy so the agent can
/// continue enforcing during transient server/network failures.
/// </summary>
public static class PolicyCacheStore
{
    public sealed record PolicyCacheEntry(
        DateTimeOffset CachedAtUtc,
        int? PolicyVersion,
        DateTimeOffset? EffectiveAtUtc,
        JsonElement? PolicySurface,
        ChildPolicy? PolicyV1);

    public static PolicyCacheEntry? Load(ChildId childId)
    {
        try
        {
            var path = CachePathFor(childId);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<PolicyCacheEntry>(json, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(ChildId childId, PolicyCacheEntry entry)
    {
        try
        {
            var path = CachePathFor(childId);
            var json = JsonSerializer.Serialize(entry, JsonDefaults.Options);
            File.WriteAllText(path, json);
        }
        catch
        {
            // best-effort
        }
    }

    private static string CachePathFor(ChildId childId)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"policy-cache.{childId.Value}.v1.json");
    }
}
