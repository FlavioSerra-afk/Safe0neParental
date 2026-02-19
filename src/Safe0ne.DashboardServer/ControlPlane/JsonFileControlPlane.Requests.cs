using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    public AccessRequest CreateOrGetRequest(ChildId childId, CreateAccessRequestRequest req)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var requestId = req.RequestId ?? Guid.NewGuid();

            var existingById = _requests.FirstOrDefault(r => r.RequestId == requestId);
            if (existingById is not null)
            {
                return existingById;
            }

            var target = string.IsNullOrWhiteSpace(req.Target)
                ? (req.Type == AccessRequestType.MoreTime ? "screen_time" : "")
                : req.Target!.Trim();

            // Small dedupe window to avoid spam (type+target+pending).
            var windowStart = now.AddMinutes(-2);
            var existingWindow = _requests
                .Where(r => r.ChildId.Value == childId.Value)
                .Where(r => r.Status == AccessRequestStatus.Pending)
                .Where(r => r.Type == req.Type)
                .Where(r => string.Equals(r.Target, target, StringComparison.OrdinalIgnoreCase))
                .Where(r => r.CreatedAtUtc >= windowStart)
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault();

            if (existingWindow is not null)
            {
                return existingWindow;
            }

            var created = new AccessRequest(
                RequestId: requestId,
                ChildId: childId,
                Type: req.Type,
                Target: target,
                Reason: string.IsNullOrWhiteSpace(req.Reason) ? null : req.Reason!.Trim(),
                CreatedAtUtc: now);

            _requests.Add(created);
            EnsureChildProfileUnsafe_NoLock(childId);

            try
            {
                var evt = JsonSerializer.Serialize(new[] { new { kind = "request_created", requestId = created.RequestId, type = created.Type.ToString(), target = created.Target, reason = created.Reason, occurredAtUtc = now.ToString("O") } }, JsonDefaults.Options);
                AppendLocalActivityJsonUnsafe_NoLock(childId.Value.ToString(), evt);
            }
            catch { }

            PersistUnsafe_NoLock();
            return created;
        }
    }

    public IReadOnlyList<AccessRequest> GetRequests(ChildId? childId = null, AccessRequestStatus? status = null, int take = 200)
    {
        lock (_gate)
        {
            IEnumerable<AccessRequest> q = _requests;
            if (childId is not null)
            {
                q = q.Where(r => r.ChildId == childId.Value);
            }
            if (status is not null)
            {
                q = q.Where(r => r.Status == status);
            }
            return q
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(Math.Clamp(take, 1, 1000))
                .ToList();
        }
    }

    public bool TryDecideRequest(Guid requestId, DecideAccessRequestRequest decision, out AccessRequest updated, out Grant? createdGrant)
    {
        lock (_gate)
        {
            createdGrant = null;
            var idx = _requests.FindIndex(r => r.RequestId == requestId);
            if (idx < 0)
            {
                updated = null!;
                return false;
            }

            var req = _requests[idx];
            if (req.Status != AccessRequestStatus.Pending)
            {
                // idempotent: return already-decided request
                updated = req;
                return true;
            }

            var now = DateTimeOffset.UtcNow;
            var decidedBy = string.IsNullOrWhiteSpace(decision.DecidedBy) ? "parent" : decision.DecidedBy!.Trim();

            var accessDecision = new AccessDecision(
                Approved: decision.Approve,
                ExtraMinutes: decision.ExtraMinutes,
                DurationMinutes: decision.DurationMinutes,
                Notes: string.IsNullOrWhiteSpace(decision.Notes) ? null : decision.Notes!.Trim());

            updated = req with
            {
                Status = decision.Approve ? AccessRequestStatus.Approved : AccessRequestStatus.Denied,
                DecidedAtUtc = now,
                DecidedBy = decidedBy,
                Decision = accessDecision
            };

            _requests[idx] = updated;

            if (decision.Approve)
            {
                createdGrant = CreateGrantUnsafe_NoLock(updated, decision, now);
                if (createdGrant is not null)
                {
                    _grants.Add(createdGrant);
                }
            }

            
            try
            {
                var events = new System.Collections.Generic.List<object>();
                events.Add(new { kind = "request_decided", requestId = updated.RequestId, approved = decision.Approve, type = updated.Type.ToString(), target = updated.Target, occurredAtUtc = now.ToString("O"), decidedBy = decidedBy, extraMinutes = decision.ExtraMinutes, durationMinutes = decision.DurationMinutes });
                if (createdGrant is not null)
                {
                    events.Add(new { kind = "grant_created", grantId = createdGrant.GrantId, type = createdGrant.Type.ToString(), target = createdGrant.Target, expiresAtUtc = createdGrant.ExpiresAtUtc.ToString("O"), occurredAtUtc = now.ToString("O"), sourceRequestId = updated.RequestId });
                }
                var evtJson = JsonSerializer.Serialize(events, JsonDefaults.Options);
                AppendLocalActivityJsonUnsafe_NoLock(updated.ChildId.Value.ToString(), evtJson);
            }
            catch { }

            PersistUnsafe_NoLock();
            return true;
        }
    }

    private int GetActiveExtraScreenTimeMinutesUnsafe_NoLock(ChildId childId, DateTimeOffset nowUtc)
    {
        return _grants
            .Where(g => g.ChildId.Value == childId.Value)
            .Where(g => g.Type == GrantType.ExtraScreenTime)
            .Where(g => g.ExpiresAtUtc > nowUtc)
            .Select(g => g.ExtraMinutes ?? 0)
            .Sum();
    }

    private static Grant? CreateGrantUnsafe_NoLock(AccessRequest req, DecideAccessRequestRequest decision, DateTimeOffset nowUtc)
    {
        switch (req.Type)
        {
            case AccessRequestType.MoreTime:
            {
                var extra = decision.ExtraMinutes ?? 15;
                extra = Math.Clamp(extra, 1, 24 * 60);

                // Expire at local midnight (Windows-first prototype: parent/child share local timezone).
                var localNow = DateTimeOffset.Now;
                var localMidnightNext = localNow.Date.AddDays(1);
                var expiresLocal = new DateTimeOffset(localMidnightNext, localNow.Offset);
                var expiresUtc = expiresLocal.ToUniversalTime();

                return new Grant(
                    GrantId: Guid.NewGuid(),
                    ChildId: req.ChildId,
                    Type: GrantType.ExtraScreenTime,
                    Target: "screen_time",
                    CreatedAtUtc: nowUtc,
                    ExpiresAtUtc: expiresUtc,
                    ExtraMinutes: extra,
                    SourceRequestId: req.RequestId);
            }
            case AccessRequestType.UnblockApp:
            {
                var dur = decision.DurationMinutes ?? 30;
                dur = Math.Clamp(dur, 1, 24 * 60);
                return new Grant(
                    GrantId: Guid.NewGuid(),
                    ChildId: req.ChildId,
                    Type: GrantType.UnblockApp,
                    Target: req.Target,
                    CreatedAtUtc: nowUtc,
                    ExpiresAtUtc: nowUtc.AddMinutes(dur),
                    ExtraMinutes: null,
                    SourceRequestId: req.RequestId);
            }
            case AccessRequestType.UnblockSite:
            {
                var dur = decision.DurationMinutes ?? 30;
                dur = Math.Clamp(dur, 1, 24 * 60);
                return new Grant(
                    GrantId: Guid.NewGuid(),
                    ChildId: req.ChildId,
                    Type: GrantType.UnblockSite,
                    Target: req.Target,
                    CreatedAtUtc: nowUtc,
                    ExpiresAtUtc: nowUtc.AddMinutes(dur),
                    ExtraMinutes: null,
                    SourceRequestId: req.RequestId);
            }
            default:
                return null;
        }
    }

}
