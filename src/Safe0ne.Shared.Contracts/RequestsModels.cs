namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Child-initiated access requests (K8/P11).
///
/// NOTE: New fields MUST be appended with defaults so older JSON can still deserialize.
/// </summary>
public enum AccessRequestType
{
    MoreTime = 0,
    UnblockApp = 1,
    UnblockSite = 2
}

public enum AccessRequestStatus
{
    Pending = 0,
    Approved = 1,
    Denied = 2,
    Cancelled = 3
}

/// <summary>
/// A request created by the child. This is stored and surfaced to the parent for approval.
/// </summary>
public sealed record AccessRequest(
    Guid RequestId,
    ChildId ChildId,
    AccessRequestType Type,
    string Target,
    string? Reason,
    DateTimeOffset CreatedAtUtc,
    AccessRequestStatus Status = AccessRequestStatus.Pending,
    DateTimeOffset? DecidedAtUtc = null,
    string? DecidedBy = null,
    AccessDecision? Decision = null);

/// <summary>
/// Parent decision on a request.
/// </summary>
public sealed record AccessDecision(
    bool Approved,
    int? ExtraMinutes = null,
    int? DurationMinutes = null,
    string? Notes = null);

public enum GrantType
{
    ExtraScreenTime = 0,
    UnblockApp = 1,
    UnblockSite = 2
}

/// <summary>
/// A time-boxed exception generated from an approved request.
/// </summary>
public sealed record Grant(
    Guid GrantId,
    ChildId ChildId,
    GrantType Type,
    string Target,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    int? ExtraMinutes = null,
    Guid? SourceRequestId = null);

/// <summary>
/// POST body for child request creation.
/// Child should provide RequestId to support idempotency.
/// </summary>
public sealed record CreateAccessRequestRequest(
    Guid? RequestId,
    AccessRequestType Type,
    string? Target = null,
    string? Reason = null);

/// <summary>
/// POST body for parent approve/deny.
/// </summary>
public sealed record DecideAccessRequestRequest(
    bool Approve,
    int? ExtraMinutes = null,
    int? DurationMinutes = null,
    string? DecidedBy = null,
    string? Notes = null);
