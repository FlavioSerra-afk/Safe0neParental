using System.Text.Json;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent;

/// <summary>
/// Local auth persistence for the child agent (device token + current child binding).
/// This is NOT SSOT; SSOT remains the DashboardServer control plane. This file is the agent's local credential cache.
/// </summary>
public static class AgentAuthStore
{
    private static string BaseDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "ChildAgent");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string CurrentChildIdPath() => Path.Combine(BaseDir(), "current.child.v1.txt");

    public static ChildId? LoadCurrentChildId()
    {
        try
        {
            var path = CurrentChildIdPath();
            if (!File.Exists(path)) return null;
            var raw = File.ReadAllText(path).Trim();
            return Guid.TryParse(raw, out var g) ? new ChildId(g) : null;
        }
        catch
        {
            return null;
        }
    }

    public static void SaveCurrentChildId(ChildId childId)
    {
        try
        {
            File.WriteAllText(CurrentChildIdPath(), childId.Value.ToString());
        }
        catch
        {
            // best effort
        }
    }

    public static string AuthPathFor(ChildId childId) => Path.Combine(BaseDir(), $"auth.{childId.Value}.v1.json");

    public static AgentAuthState? LoadAuth(ChildId childId)
    {
        try
        {
            var path = AuthPathFor(childId);
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<AgentAuthState>(json, JsonDefaults.Options);
        }
        catch
        {
            return null;
        }
    }

    public static void SaveAuth(ChildId childId, AgentAuthState state)
    {
        try
        {
            var path = AuthPathFor(childId);
            var json = JsonSerializer.Serialize(state, JsonDefaults.Options);
            File.WriteAllText(path, json);
        }
        catch
        {
            // best effort
        }
    }

    public sealed record AgentAuthState(
        Guid DeviceId,
        string DeviceToken,
        DateTimeOffset IssuedAtUtc);
}
