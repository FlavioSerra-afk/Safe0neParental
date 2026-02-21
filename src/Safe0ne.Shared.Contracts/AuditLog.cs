using System;
using System.Collections.Generic;

namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Append-only audit entry for local control plane policy/settings/device actions.
/// Privacy-first: payload is intentionally redacted; tamper-evident chaining is supported via hash fields.
/// </summary>
public sealed record AuditEntry(
    string ChildId,
    DateTimeOffset OccurredAtUtc,
    string Actor,
    string Action,
    string Scope,
    string? BeforeHashSha256,
    string AfterHashSha256);

/// <summary>
/// Audit log envelope returned by local endpoints.
/// </summary>
public sealed record AuditLogEnvelope(
    string ChildId,
    IReadOnlyList<AuditEntry> Entries);

/// <summary>
/// Response for local audit retention/purge operations.
/// Additive-only: used by local endpoints and UI tooling.
/// </summary>
public sealed record AuditPurgeResponse(
    string ChildId,
    DateTimeOffset OlderThanUtc,
    int DeletedCount);
