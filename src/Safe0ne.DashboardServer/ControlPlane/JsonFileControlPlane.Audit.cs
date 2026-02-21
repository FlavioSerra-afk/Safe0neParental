using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    // Local mode: audit entries persisted as JSON arrays per child.
    private Dictionary<string, string> _localAuditEntriesJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Return append-only audit entries for the given child (newest-first). Best-effort.
    /// </summary>
    public AuditLogEnvelope GetLocalAuditLog(ChildId childId, int take)
    {
        return QueryLocalAuditLog(childId,
            take: take,
            fromUtc: null,
            toUtc: null,
            actorContains: null,
            actionContains: null,
            scopeContains: null,
            q: null);
    }

    /// <summary>
    /// Query audit entries with best-effort filtering (newest-first in response).
    /// Intended for UI polish and diagnostics exports.
    /// </summary>
    public AuditLogEnvelope QueryLocalAuditLog(
        ChildId childId,
        int take,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? actorContains,
        string? actionContains,
        string? scopeContains,
        string? q)
    {
        if (take <= 0) take = 200;
        if (take > 1000) take = 1000;

        static bool ContainsCi(string haystack, string? needle)
        {
            if (string.IsNullOrWhiteSpace(needle)) return true;
            return (haystack ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_localAuditEntriesJsonByChildGuid.TryGetValue(key, out var json) || string.IsNullOrWhiteSpace(json))
            {
                return new AuditLogEnvelope(key, Array.Empty<AuditEntry>());
            }

            try
            {
                var entries = JsonSerializer.Deserialize<List<AuditEntry>>(json, JsonDefaults.Options) ?? new List<AuditEntry>();

                var filtered = entries
                    .Where(e => fromUtc is null || e.OccurredAtUtc >= fromUtc.Value)
                    .Where(e => toUtc is null || e.OccurredAtUtc <= toUtc.Value)
                    .Where(e => ContainsCi(e.Actor, actorContains))
                    .Where(e => ContainsCi(e.Action, actionContains))
                    .Where(e => ContainsCi(e.Scope, scopeContains));

                if (!string.IsNullOrWhiteSpace(q))
                {
                    filtered = filtered.Where(e =>
                        ContainsCi(e.Actor, q) ||
                        ContainsCi(e.Action, q) ||
                        ContainsCi(e.Scope, q) ||
                        ContainsCi(e.BeforeHashSha256 ?? string.Empty, q) ||
                        ContainsCi(e.AfterHashSha256 ?? string.Empty, q));
                }

                var slice = filtered
                    .OrderByDescending(e => e.OccurredAtUtc)
                    .Take(take)
                    .ToList();

                return new AuditLogEnvelope(key, slice);
            }
            catch
            {
                return new AuditLogEnvelope(key, Array.Empty<AuditEntry>());
            }
        }
    }

    /// <summary>
    /// Purge audit entries older than the given UTC threshold (best-effort).
    /// Returns the number of deleted entries.
    /// </summary>
    public int PurgeLocalAuditEntriesOlderThan(ChildId childId, DateTimeOffset olderThanUtc)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_localAuditEntriesJsonByChildGuid.TryGetValue(key, out var json) || string.IsNullOrWhiteSpace(json))
                return 0;

            try
            {
                var entries = JsonSerializer.Deserialize<List<AuditEntry>>(json, JsonDefaults.Options) ?? new List<AuditEntry>();
                var before = entries.Count;

                // Keep entries at/after threshold.
                entries = entries.Where(e => e.OccurredAtUtc >= olderThanUtc).ToList();
                var after = entries.Count;

                _localAuditEntriesJsonByChildGuid[key] = JsonSerializer.Serialize(entries, JsonDefaults.Options);
                PersistUnsafe_NoLock();

                return Math.Max(0, before - after);
            }
            catch
            {
                return 0;
            }
        }
    }

    /// <summary>
    /// Append an audit entry for a child. Uses a simple tamper-evident chain: BeforeHash = previous AfterHash.
    /// </summary>
    public void AppendLocalAuditEntry(ChildId childId, string actor, string action, string scope, string redactedPayload)
    {
        if (string.IsNullOrWhiteSpace(actor)) actor = "unknown";
        if (string.IsNullOrWhiteSpace(action)) action = "unknown";
        if (string.IsNullOrWhiteSpace(scope)) scope = "unknown";

        lock (_gate)
        {
            var key = childId.Value.ToString();
            var now = DateTimeOffset.UtcNow;

            var entries = new List<AuditEntry>();
            if (_localAuditEntriesJsonByChildGuid.TryGetValue(key, out var existing) && !string.IsNullOrWhiteSpace(existing))
            {
                try { entries = JsonSerializer.Deserialize<List<AuditEntry>>(existing, JsonDefaults.Options) ?? new List<AuditEntry>(); }
                catch { entries = new List<AuditEntry>(); }
            }

            // Chain: before is previous after hash (most recent by time).
            var prevAfter = entries
                .OrderByDescending(e => e.OccurredAtUtc)
                .FirstOrDefault()
                ?.AfterHashSha256;

            var afterHash = Sha256Hex(redactedPayload);

            entries.Add(new AuditEntry(
                ChildId: key,
                OccurredAtUtc: now,
                Actor: actor,
                Action: action,
                Scope: scope,
                BeforeHashSha256: prevAfter,
                AfterHashSha256: afterHash));

            // Retention: keep last 1000 by time.
            entries = entries
                .OrderByDescending(e => e.OccurredAtUtc)
                .Take(1000)
                .OrderBy(e => e.OccurredAtUtc)
                .ToList();

            _localAuditEntriesJsonByChildGuid[key] = JsonSerializer.Serialize(entries, JsonDefaults.Options);
            PersistUnsafe_NoLock();
        }
    }

    private static string Sha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
