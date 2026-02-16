using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Requests;

/// <summary>
/// K8: local persistent request outbox.
/// - Queues requests when the Control Plane is offline.
/// - Retries with exponential backoff.
/// - Dedupes near-identical requests within a short window.
///
/// Outbox stores ONLY unsent requests. Once the server accepts a request,
/// it is removed from the outbox, but we keep a small local "recent" list
/// to show status in the child UX.
/// </summary>
public sealed class AccessRequestQueue
{
    private readonly object _lock = new();
    private readonly ILogger<AccessRequestQueue> _logger;

    private readonly string _outboxPath;
    private readonly string _recentPath;

    private DateTimeOffset? _lastFlushAttemptUtc;
    private string? _lastFlushError;

    private static readonly TimeSpan DedupeWindow = TimeSpan.FromMinutes(2);

    public AccessRequestQueue(ILogger<AccessRequestQueue> logger)
    {
        _logger = logger;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(dir);

        _outboxPath = Path.Combine(dir, "requests.outbox.v1.json");
        _recentPath = Path.Combine(dir, "requests.recent.v1.json");
    }

    public EnqueueResult Enqueue(ChildId childId, AccessRequestType type, string target, string? reason, Guid? requestId = null)
    {
        lock (_lock)
        {
            var now = DateTimeOffset.UtcNow;
            var rid = requestId ?? Guid.NewGuid();

            var outbox = LoadOutbox_NoThrow() ?? new OutboxState(Array.Empty<OutboxItem>());
            var recent = LoadRecent_NoThrow() ?? new RecentState(Array.Empty<RecentItem>());

            // Deduplicate by (type,target) within a short window if still pending.
            var dupRecent = recent.Items.FirstOrDefault(i =>
                i.ChildId == childId.Value &&
                i.Type == type &&
                string.Equals(i.Target, target, StringComparison.OrdinalIgnoreCase) &&
                (now - i.CreatedAtUtc) <= DedupeWindow &&
                i.Status == AccessRequestStatus.Pending);

            if (dupRecent is not null)
            {
                return new EnqueueResult(EnqueueOutcome.Deduped, dupRecent.RequestId);
            }

            var dupOutbox = outbox.Items.FirstOrDefault(i =>
                i.Request.ChildId.Value == childId.Value &&
                i.Request.Type == type &&
                string.Equals(i.Request.Target, target, StringComparison.OrdinalIgnoreCase) &&
                (now - i.Request.CreatedAtUtc) <= DedupeWindow);

            if (dupOutbox is not null)
            {
                return new EnqueueResult(EnqueueOutcome.Deduped, dupOutbox.Request.RequestId);
            }

            var req = new AccessRequest(
                RequestId: rid,
                ChildId: childId,
                Type: type,
                Target: target,
                Reason: reason,
                CreatedAtUtc: now);

            var item = new OutboxItem(
                Request: req,
                NextAttemptUtc: now,
                AttemptCount: 0);

            outbox = outbox with { Items = outbox.Items.Append(item).ToArray() };
            recent = UpsertRecent(recent, req);

            SaveOutbox_NoThrow(outbox);
            SaveRecent_NoThrow(recent);

            return new EnqueueResult(EnqueueOutcome.Enqueued, rid);
        }
    }

    /// <summary>
    /// Best-effort flush of queued requests to the control plane + status sync.
    /// Safe to call frequently (e.g. on every heartbeat tick).
    /// </summary>
    public async Task FlushAndSyncAsync(HttpClient client, ChildId childId, CancellationToken ct)
    {
        try
        {
            lock (_lock)
            {
                _lastFlushAttemptUtc = DateTimeOffset.UtcNow;
            }
            await FlushAsync(client, childId, ct).ConfigureAwait(false);
            await SyncStatusesAsync(client, childId, ct).ConfigureAwait(false);

            lock (_lock)
            {
                _lastFlushError = null;
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _lastFlushError = ex.Message;
            }
            _logger.LogDebug(ex, "Request queue flush/sync failed (best effort)");
        }
    }

    public IReadOnlyList<RecentItem> GetRecent(ChildId childId, int max = 5)
    {
        lock (_lock)
        {
            var recent = LoadRecent_NoThrow();
            if (recent is null) return Array.Empty<RecentItem>();

            return recent.Items
                .Where(i => i.ChildId == childId.Value)
                .OrderByDescending(i => i.CreatedAtUtc)
                .Take(Math.Clamp(max, 1, 20))
                .ToArray();
        }
    }

    private async Task FlushAsync(HttpClient client, ChildId childId, CancellationToken ct)
    {
        OutboxState? outbox;
        lock (_lock)
        {
            outbox = LoadOutbox_NoThrow();
        }

        if (outbox is null || outbox.Items.Length == 0) return;

        var now = DateTimeOffset.UtcNow;
        var changed = false;
        var items = outbox.Items.ToList();

        for (int idx = 0; idx < items.Count; idx++)
        {
            ct.ThrowIfCancellationRequested();

            var item = items[idx];
            if (item.Request.ChildId.Value != childId.Value) continue;
            if (item.NextAttemptUtc > now) continue;

            var body = new CreateAccessRequestRequest(
                RequestId: item.Request.RequestId,
                Type: item.Request.Type,
                Target: item.Request.Target,
                Reason: item.Request.Reason);

            try
            {
                // Local mode first. Fall back to v1 if local endpoints are not available.
                using var resp = await PostRequestLocalFirstAsync(client, childId, body, ct).ConfigureAwait(false);

                if (resp.IsSuccessStatusCode)
                {
                    // accepted => remove from outbox. Status will be refreshed by Sync.
                    items.RemoveAt(idx);
                    idx--;
                    changed = true;
                    continue;
                }

                // Treat 409 as "already exists" => remove from outbox (idempotent accept).
                if ((int)resp.StatusCode == 409)
                {
                    items.RemoveAt(idx);
                    idx--;
                    changed = true;
                    continue;
                }

                // otherwise retry later
                var next = ComputeNextAttemptUtc(item.AttemptCount + 1, now);
                items[idx] = item with { AttemptCount = item.AttemptCount + 1, NextAttemptUtc = next };
                changed = true;
            }
            catch
            {
                // network/offline => retry later
                var next = ComputeNextAttemptUtc(item.AttemptCount + 1, now);
                items[idx] = item with { AttemptCount = item.AttemptCount + 1, NextAttemptUtc = next };
                changed = true;
            }
        }

        if (changed)
        {
            lock (_lock)
            {
                SaveOutbox_NoThrow(new OutboxState(items.ToArray()));
            }
        }
    }

    private async Task SyncStatusesAsync(HttpClient client, ChildId childId, CancellationToken ct)
    {
        var res = await GetRequestsLocalFirstAsync(client, childId, take: 20, ct).ConfigureAwait(false);
        if (res?.Ok != true || res.Data is null) return;

        lock (_lock)
        {
            var recent = LoadRecent_NoThrow() ?? new RecentState(Array.Empty<RecentItem>());
            foreach (var r in res.Data)
            {
                recent = UpsertRecent(recent, r);
            }

            // trim
            recent = recent with
            {
                Items = recent.Items
                    .OrderByDescending(i => i.CreatedAtUtc)
                    .Take(50)
                    .ToArray()
            };

            SaveRecent_NoThrow(recent);
        }
    }

    private static async Task<HttpResponseMessage> PostRequestLocalFirstAsync(
        HttpClient client,
        ChildId childId,
        CreateAccessRequestRequest body,
        CancellationToken ct)
    {
        // 1) Local contract (preferred)
        try
        {
            var resp = await client.PostAsJsonAsync(
                $"/api/local/children/{childId.Value}/requests",
                body,
                JsonDefaults.Options,
                ct).ConfigureAwait(false);

            if (resp.IsSuccessStatusCode || (int)resp.StatusCode == 409)
            {
                return resp;
            }

            // If local returns a definitive non-success (other than 404), keep it.
            if (resp.StatusCode != System.Net.HttpStatusCode.NotFound)
            {
                return resp;
            }

            // 404 => local API not present, fall through.
            resp.Dispose();
        }
        catch
        {
            // network/offline => fall through to v1 attempt
        }

        // 2) Existing v1 API
        return await client.PostAsJsonAsync(
            $"/api/{ApiVersions.V1}/children/{childId.Value}/requests",
            body,
            JsonDefaults.Options,
            ct).ConfigureAwait(false);
    }

    private static async Task<ApiResponse<List<AccessRequest>>?> GetRequestsLocalFirstAsync(
        HttpClient client,
        ChildId childId,
        int take,
        CancellationToken ct)
    {
        // 1) Local contract (preferred)
        try
        {
            var local = await client.GetFromJsonAsync<ApiResponse<List<AccessRequest>>>(
                $"/api/local/children/{childId.Value}/requests?take={Math.Clamp(take, 1, 200)}",
                JsonDefaults.Options,
                ct).ConfigureAwait(false);

            if (local is not null) return local;
        }
        catch
        {
            // fall through
        }

        // 2) Existing v1 API
        try
        {
            return await client.GetFromJsonAsync<ApiResponse<List<AccessRequest>>>(
                $"/api/{ApiVersions.V1}/children/{childId.Value}/requests?take={Math.Clamp(take, 1, 200)}",
                JsonDefaults.Options,
                ct).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private static DateTimeOffset ComputeNextAttemptUtc(int attempt, DateTimeOffset nowUtc)
    {
        // exponential backoff: 5s, 10s, 20s, 40s, 60s, 60s...
        var seconds = Math.Min(60, 5 * (int)Math.Pow(2, Math.Clamp(attempt - 1, 0, 6)));
        return nowUtc.AddSeconds(seconds);
    }

    private static RecentState UpsertRecent(RecentState state, AccessRequest r)
    {
        var item = new RecentItem(
            RequestId: r.RequestId,
            ChildId: r.ChildId.Value,
            Type: r.Type,
            Target: r.Target,
            Reason: r.Reason,
            CreatedAtUtc: r.CreatedAtUtc,
            Status: r.Status,
            DecidedAtUtc: r.DecidedAtUtc);

        var existing = state.Items.FirstOrDefault(i => i.RequestId == r.RequestId);
        if (existing is null)
        {
            return state with { Items = state.Items.Append(item).ToArray() };
        }

        var replaced = state.Items.Select(i => i.RequestId == r.RequestId ? item : i).ToArray();
        return state with { Items = replaced };
    }



public RequestQueueTelemetry GetTelemetry(ChildId childId)
{
    lock (_lock)
    {
        var outbox = LoadOutbox_NoThrow();
        if (outbox is null || outbox.Items.Length == 0)
        {
            return new RequestQueueTelemetry(
                HasPendingOutbox: false,
                NextAttemptUtc: null,
                LastAttemptUtc: _lastFlushAttemptUtc,
                LastError: _lastFlushError);
        }

        var next = outbox.Items
            .Where(i => i.Request.ChildId.Value == childId.Value)
            .Select(i => i.NextAttemptUtc)
            .DefaultIfEmpty()
            .Min();

        var has = outbox.Items.Any(i => i.Request.ChildId.Value == childId.Value);

        return new RequestQueueTelemetry(
            HasPendingOutbox: has,
            NextAttemptUtc: has ? next : null,
            LastAttemptUtc: _lastFlushAttemptUtc,
            LastError: _lastFlushError);
    }
}

public bool IsQueued(ChildId childId, Guid requestId)
{
    lock (_lock)
    {
        var outbox = LoadOutbox_NoThrow();
        if (outbox is null) return false;
        return outbox.Items.Any(i => i.Request.ChildId.Value == childId.Value && i.Request.RequestId == requestId);
    }
}

public sealed record RequestQueueTelemetry(
    bool HasPendingOutbox,
    DateTimeOffset? NextAttemptUtc,
    DateTimeOffset? LastAttemptUtc,
    string? LastError);
    private OutboxState? LoadOutbox_NoThrow()
    {
        try
        {
            if (!File.Exists(_outboxPath)) return null;
            var json = File.ReadAllText(_outboxPath);
            return JsonSerializer.Deserialize<OutboxState>(json, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    private void SaveOutbox_NoThrow(OutboxState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, JsonDefaults.Options);
            File.WriteAllText(_outboxPath, json);
        }
        catch { }
    }

    private RecentState? LoadRecent_NoThrow()
    {
        try
        {
            if (!File.Exists(_recentPath)) return null;
            var json = File.ReadAllText(_recentPath);
            return JsonSerializer.Deserialize<RecentState>(json, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    private void SaveRecent_NoThrow(RecentState state)
    {
        try
        {
            var json = JsonSerializer.Serialize(state, JsonDefaults.Options);
            File.WriteAllText(_recentPath, json);
        }
        catch { }
    }

    private sealed record OutboxState(OutboxItem[] Items);

    private sealed record OutboxItem(
        AccessRequest Request,
        DateTimeOffset NextAttemptUtc,
        int AttemptCount);

    public sealed record RecentState(RecentItem[] Items);

    public sealed record RecentItem(
        Guid RequestId,
        Guid ChildId,
        AccessRequestType Type,
        string Target,
        string? Reason,
        DateTimeOffset CreatedAtUtc,
        AccessRequestStatus Status,
        DateTimeOffset? DecidedAtUtc);

    public sealed record EnqueueResult(EnqueueOutcome Outcome, Guid RequestId);

    public enum EnqueueOutcome
    {
        Enqueued = 0,
        Deduped = 1
    }
}
