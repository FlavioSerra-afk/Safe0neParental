using System.Security.Cryptography;
using System.Text;
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
        ChildPolicy? PolicyV1,
        // 26W09L: integrity check (best-effort). If present, Load validates it.
        string? ChecksumSha256 = null);

    public sealed record LoadResult(PolicyCacheEntry? Entry, bool IntegrityOk);

    public static LoadResult Load(ChildId childId)
    {
        try
        {
            var path = CachePathFor(childId);
            if (!File.Exists(path)) return new LoadResult(null, true);
            var json = File.ReadAllText(path);
            var entry = JsonSerializer.Deserialize<PolicyCacheEntry>(json, JsonDefaults.Options);
            if (entry is null) return new LoadResult(null, true);

            // If checksum is absent, treat as ok (backward compatible with older cache files).
            if (string.IsNullOrWhiteSpace(entry.ChecksumSha256))
                return new LoadResult(entry, true);

            var expected = ComputeChecksumSha256(entry);
            var ok = string.Equals(expected, entry.ChecksumSha256, StringComparison.OrdinalIgnoreCase);
            return new LoadResult(ok ? entry : null, ok);
        }
        catch
        {
            return new LoadResult(null, false);
        }
    }

    public static void Save(ChildId childId, PolicyCacheEntry entry)
    {
        try
        {
            var path = CachePathFor(childId);
            // Rotate backup on each write so we can roll back if a new policy breaks enforcement.
            TryRotateBackup(path);

            var withChecksum = entry with
            {
                ChecksumSha256 = ComputeChecksumSha256(entry)
            };

            var json = JsonSerializer.Serialize(withChecksum, JsonDefaults.Options);
            File.WriteAllText(path, json);
        }
        catch
        {
            // best-effort
        }
    }

    public static bool RestoreBackup(ChildId childId)
    {
        try
        {
            var path = CachePathFor(childId);
            var bak = path + ".bak";
            if (!File.Exists(bak)) return false;
            File.Copy(bak, path, overwrite: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Delete(ChildId childId)
    {
        try
        {
            var path = CachePathFor(childId);
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // best-effort
        }
    }

    private static void TryRotateBackup(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            File.Copy(path, path + ".bak", overwrite: true);
        }
        catch
        {
            // best-effort
        }
    }

    private static string ComputeChecksumSha256(PolicyCacheEntry entry)
    {
        // Important: hash only the “content” fields, not the checksum itself.
        // Keep this stable over time: if we add new fields, they must be included here
        // (or we intentionally accept old checksum as absent).
        var payload = new
        {
            entry.CachedAtUtc,
            entry.PolicyVersion,
            entry.EffectiveAtUtc,
            PolicySurface = entry.PolicySurface,
            entry.PolicyV1
        };

        var json = JsonSerializer.Serialize(payload, JsonDefaults.Options);
        var bytes = Encoding.UTF8.GetBytes(json);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
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
