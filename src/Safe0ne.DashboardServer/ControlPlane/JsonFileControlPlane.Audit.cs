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
        if (take <= 0) take = 200;
        if (take > 1000) take = 1000;

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
                var slice = entries
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
