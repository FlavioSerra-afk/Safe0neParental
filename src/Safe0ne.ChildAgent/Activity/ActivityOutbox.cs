using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.Activity;

public sealed class ActivityOutbox
{
    private readonly string _path;
    private readonly object _gate = new();
    private State _state;

    public ActivityOutbox(ChildId childId)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(dir);

        _path = Path.Combine(dir, $"activity.outbox.{childId.Value}.v1.json");
        _state = LoadUnsafe() ?? new State(new List<LocalActivityEvent>());
    }

    public int Count
    {
        get
        {
            lock (_gate) return _state.Events.Count;
        }
    }

    public void Enqueue(LocalActivityEvent ev)
    {
        lock (_gate)
        {
            const int maxQueued = 500;
            if (_state.Events.Count >= maxQueued)
            {
                var remove = Math.Max(1, _state.Events.Count - maxQueued + 1);
                _state.Events.RemoveRange(0, remove);
            }

            _state.Events.Add(ev);
            SaveUnsafe(_state);
        }
    }

    public async Task FlushAsync(HttpClient client, ChildId childId, CancellationToken ct)
    {
        List<LocalActivityEvent> batch;
        lock (_gate)
        {
            if (_state.Events.Count == 0) return;
            batch = _state.Events.Take(50).ToList();
        }

        // Local mode first
        if (await TryPostAsync(client, $"/api/local/children/{childId.Value}/activity", new PostActivityRequest(batch), ct))
        {
            lock (_gate)
            {
                _state = new State(_state.Events.Skip(batch.Count).ToList());
                SaveUnsafe(_state);
            }
            return;
        }

        // Legacy fallback (safe no-op if endpoint doesn't exist)
        await TryPostAsync(client, $"/api/{ApiVersions.V1}/children/{childId.Value}/activity", new PostActivityRequest(batch), ct);
    }

    private static async Task<bool> TryPostAsync(HttpClient client, string url, PostActivityRequest body, CancellationToken ct)
    {
        try
        {
            using var resp = await client.PostAsJsonAsync(url, body, JsonDefaults.Options, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound) return false;
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private State? LoadUnsafe()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<State>(json, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    private void SaveUnsafe(State s)
    {
        try
        {
            var json = JsonSerializer.Serialize(s, JsonDefaults.Options);
            File.WriteAllText(_path, json);
        }
        catch
        {
            // ignore (best-effort)
        }
    }

    public sealed record LocalActivityEvent(
        Guid EventId,
        DateTimeOffset OccurredAtUtc,
        string Kind,
        string? App,
        string? Details,
        string? DeviceId);

    public sealed record PostActivityRequest(List<LocalActivityEvent> Events);

    private sealed record State(List<LocalActivityEvent> Events);
}
