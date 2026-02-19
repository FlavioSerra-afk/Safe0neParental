using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// File-backed control plane store.
/// Stores a small JSON snapshot under the current user's LocalApplicationData.
/// </summary>
public sealed partial class JsonFileControlPlane
{

private const int CurrentSchemaVersion = 2;
    private const int MinSupportedSchemaVersion = 1;


private readonly object _gate = new();
    private readonly string _path;

    private List<ChildProfile> _children = new();
    private Dictionary<string, DateTimeOffset> _archivedAtByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ChildPolicy> _policiesByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, ChildAgentStatus> _statusByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    
    // Local mode: UI metadata + settings profile blobs (stored as JSON).
    private Dictionary<string, string> _localChildMetaJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _localSettingsProfileJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // Local mode: activity events (stored as JSON arrays per child).
    private Dictionary<string, string> _localActivityEventsJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // Local mode: last known location (stored as JSON object per child).
    private Dictionary<string, string> _localLastLocationJsonByChildGuid = new(StringComparer.OrdinalIgnoreCase);

// K1: per-child paired devices and pending pairing codes.
    private Dictionary<string, List<PairedDevice>> _devicesByChildGuid = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, PendingPairing> _pendingPairingByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // K2: queued commands (control plane -> agent).
    private Dictionary<string, List<ChildCommand>> _commandsByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    // K8/P11: access requests + grants.
    private List<AccessRequest> _requests = new();
    private List<Grant> _grants = new();

    // K9: diagnostics bundles (latest per child).
    private Dictionary<string, DiagnosticsBundleInfo> _diagnosticsByChildGuid = new(StringComparer.OrdinalIgnoreCase);

    public JsonFileControlPlane()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "DashboardServer");

        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "control-plane.v1.json");

        LoadOrSeed();
    }


    public (bool ok, string? error) TryHealthCheck()
    {
        lock (_gate)
        {
            try
            {
                // Ensure current in-memory state is loadable and the directory is writable.
                var dir = Path.GetDirectoryName(_path) ?? string.Empty;
                Directory.CreateDirectory(dir);

                var probe = Path.Combine(dir, "health.probe");
                File.WriteAllText(probe, DateTimeOffset.UtcNow.ToString("O"));
                File.Delete(probe);

                // Read touch: should not throw.
                _ = _children.Count;
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }

    public object GetInfo()
    {
        lock (_gate)
        {
            return new
            {
                ok = true,
                backend = "json",
                schema = "control-plane.v1.json",
                schemaVersion = CurrentSchemaVersion,
                storagePath = _path,
                childrenCount = _children.Count,
                requestsCount = _requests.Count,
                atUtc = DateTimeOffset.UtcNow
            };
        }
    }




            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return "[]";
        return DefaultLocationJson();
    }
}

/// <summary>
/// Upsert last known location JSON for a child. Stored as a JSON object.
/// </summary>
public void UpsertLocalLocationJson(ChildId childId, string locationJson)
{
    var key = childId.Value.ToString();
    lock (_gate)
    {
        // Validate it's a JSON object.
        try
        {
            using var doc = JsonDocument.Parse(locationJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Location payload must be a JSON object.");
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // Ignore invalid payloads.
            return;
        }

        EnsureChildProfileUnsafe_NoLock(childId);
        _localLastLocationJsonByChildGuid[key] = locationJson;
        PersistUnsafe_NoLock();
    }
}

private static string DefaultLocationJson()
    => @"{""available"":false,""capturedAtUtc"":null,""latitude"":null,""longitude"":null,""accuracyMeters"":null,""source"":null,""note"":null}";

                


public ChildProfile CreateChild(string displayName)
{
    if (string.IsNullOrWhiteSpace(displayName))
        throw new ArgumentException("DisplayName is required.", nameof(displayName));

    lock (_gate)
    {
        var id = new ChildId(Guid.NewGuid());
        var profile = new ChildProfile(id, displayName.Trim());
        _children.Add(profile);

        // Seed policy if missing.
        if (!_policiesByChildGuid.ContainsKey(id.Value.ToString()))
        {
            _policiesByChildGuid[id.Value.ToString()] = new ChildPolicy(
                ChildId: id,
                Version: new PolicyVersion(1),
                Mode: SafetyMode.Open,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedBy: "local");
        }

        PersistUnsafe_NoLock();
        return profile;
    }
}

public ChildProfile UpdateChild(ChildId childId, string? displayName, bool? archived)
{
    lock (_gate)
    {
        var idx = _children.FindIndex(c => c.Id.Value == childId.Value);
        if (idx < 0)
        {
            throw new InvalidOperationException("Child not found.");
        }

        var current = _children[idx];
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            current = current with { DisplayName = displayName.Trim() };
            _children[idx] = current;
        }

        var key = childId.Value.ToString();
        if (archived is not null)
        {
            if (archived.Value)
            {
                _archivedAtByChildGuid[key] = DateTimeOffset.UtcNow;
            }
            else
            {
                _archivedAtByChildGuid.Remove(key);
            }
        }

        PersistUnsafe_NoLock();
        return current;
    }
}

public bool IsArchived(ChildId childId)
{
    lock (_gate)
    {
        return _archivedAtByChildGuid.ContainsKey(childId.Value.ToString());
    }
}

public bool TryCompletePairingByCode(PairingCompleteRequest req, out PairingCompleteResponse resp)
{
    lock (_gate)
    {
        CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset.UtcNow);

        // Basic input hygiene.
        if (string.IsNullOrWhiteSpace(req.PairingCode))
        {
            resp = default!;
            return false;
        }

        // Find any pending pairing with this code.
        var pending = _pendingPairingByChildGuid.Values.FirstOrDefault(p =>
            string.Equals(p.PairingCode, req.PairingCode, StringComparison.Ordinal));

        if (pending is null)
        {
            resp = default!;
            return false;
        }

        resp = CompletePairing(pending.ChildId, req);
        return true;
    }
}

public bool TryUnpairDevice(Guid deviceId, out ChildId childId)
{
    lock (_gate)
    {
        childId = default;
        foreach (var kvp in _devicesByChildGuid)
        {
            var devices = kvp.Value;
            var idx = devices.FindIndex(d => d.DeviceId == deviceId);
            if (idx < 0) continue;

            devices.RemoveAt(idx);
            childId = new ChildId(Guid.Parse(kvp.Key));
            PersistUnsafe_NoLock();
            return true;
        }
        return false;
    }
}

public bool TryGetPendingPairing(ChildId childId, out PairingStartResponse resp)
{
    lock (_gate)
    {
        CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset.UtcNow);

        var key = childId.Value.ToString();
        if (!_pendingPairingByChildGuid.TryGetValue(key, out var pending))
        {
            resp = default!;
            return false;
        }

        resp = new PairingStartResponse(childId, pending.PairingCode, pending.ExpiresAtUtc);
        return true;
    }
}


    public bool TryGetPolicy(ChildId childId, out ChildPolicy policy)
    {
        lock (_gate)
        {
            return _policiesByChildGuid.TryGetValue(childId.Value.ToString(), out policy!);
        }
    }

    public bool TryGetStatus(ChildId childId, out ChildAgentStatus status)
    {
        lock (_gate)
        {
            return _statusByChildGuid.TryGetValue(childId.Value.ToString(), out status!);
        }
    }

    public IReadOnlyList<ChildDeviceSummary> GetDevices(ChildId childId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_devicesByChildGuid.TryGetValue(key, out var devices))
            {
                return Array.Empty<ChildDeviceSummary>();
            }

            DateTimeOffset? lastSeen = null;
            if (_statusByChildGuid.TryGetValue(key, out var s))
            {
                lastSeen = s.LastSeenUtc;
            }

            return devices
                .Select(d => new ChildDeviceSummary(
                    DeviceId: d.DeviceId,
                    DeviceName: d.DeviceName,
                    AgentVersion: d.AgentVersion,
                    PairedAtUtc: d.PairedAtUtc,
                    LastSeenUtc: lastSeen))
                .ToList();
        }
    }

    public bool HasPairedDevices(ChildId childId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            return _devicesByChildGuid.TryGetValue(key, out var devices) && devices.Count > 0;
        }
    }

    public bool TryValidateDeviceToken(ChildId childId, string token, out Guid deviceId)
    {
        lock (_gate)
        {
            deviceId = default;
            var key = childId.Value.ToString();
            if (!_devicesByChildGuid.TryGetValue(key, out var devices) || devices.Count == 0)
            {
                return false;
            }

            var hash = ComputeSha256Hex(token);
            var match = devices.FirstOrDefault(d => string.Equals(d.TokenHashSha256, hash, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                return false;
            }

            deviceId = match.DeviceId;
            return true;
        }
    }

    public PairingStartResponse StartPairing(ChildId childId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset.UtcNow);

            // Ensure code uniqueness across all currently pending sessions.
            string code;
            int attempts = 0;
            do
            {
                attempts++;
                code = GenerateNumericCode(6);

                // Extremely unlikely, but guard against pathological RNG / collisions.
                if (attempts > 25)
                {
                    code = GenerateNumericCode(8);
                    break;
                }
            } while (_pendingPairingByChildGuid.Values.Any(p => string.Equals(p.PairingCode, code, StringComparison.Ordinal)));

            var expires = DateTimeOffset.UtcNow.AddMinutes(10);

            _pendingPairingByChildGuid[key] = new PendingPairing(childId, code, expires);

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();

            return new PairingStartResponse(childId, code, expires);
        }
    }

    private void CleanupExpiredPairingsUnsafe_NoLock(DateTimeOffset nowUtc)
    {
        // Remove any expired pairing sessions to avoid stale codes lingering.
        // Caller must hold _gate.
        if (_pendingPairingByChildGuid.Count == 0)
        {
            return;
        }

        var expiredKeys = _pendingPairingByChildGuid
            .Where(kvp => kvp.Value.ExpiresAtUtc < nowUtc)
            .Select(kvp => kvp.Key)
            .ToList();

        if (expiredKeys.Count == 0)
        {
            return;
        }

        foreach (var k in expiredKeys)
        {
            _pendingPairingByChildGuid.Remove(k);
        }

        PersistUnsafe_NoLock();
    }

    public PairingCompleteResponse CompletePairing(ChildId childId, PairingCompleteRequest req)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_pendingPairingByChildGuid.TryGetValue(key, out var pending))
            {
                throw new InvalidOperationException("No pending pairing session.");
            }

            if (pending.ExpiresAtUtc < DateTimeOffset.UtcNow)
            {
                _pendingPairingByChildGuid.Remove(key);
                PersistUnsafe_NoLock();
                throw new InvalidOperationException("Pairing code expired.");
            }

            if (!string.Equals(pending.PairingCode, req.PairingCode, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Invalid pairing code.");
            }

            // Validate and normalize device info.
            var deviceName = (req.DeviceName ?? string.Empty).Trim();
            if (deviceName.Length == 0)
            {
                deviceName = "Kid Device";
            }
            if (deviceName.Length > 64)
            {
                deviceName = deviceName[..64];
            }

            var agentVersion = (req.AgentVersion ?? string.Empty).Trim();
            if (agentVersion.Length == 0)
            {
                agentVersion = "unknown";
            }
            if (agentVersion.Length > 32)
            {
                agentVersion = agentVersion[..32];
            }

            var deviceId = Guid.NewGuid();
            var token = GenerateDeviceToken();
            var tokenHash = ComputeSha256Hex(token);

            if (!_devicesByChildGuid.TryGetValue(key, out var devices))
            {
                devices = new List<PairedDevice>();
                _devicesByChildGuid[key] = devices;
            }

            // Defensive: ensure no duplicate DeviceId (should never happen).
            while (devices.Any(d => d.DeviceId == deviceId))
            {
                deviceId = Guid.NewGuid();
            }

            devices.Add(new PairedDevice(
                DeviceId: deviceId,
                DeviceName: deviceName,
                AgentVersion: agentVersion,
                PairedAtUtc: DateTimeOffset.UtcNow,
                TokenHashSha256: tokenHash));

            // One-time code is consumed.
            _pendingPairingByChildGuid.Remove(key);

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();

            return new PairingCompleteResponse(childId, deviceId, token, DateTimeOffset.UtcNow);
        }
    }

    public ChildAgentStatus UpsertStatus(ChildId childId, ChildAgentHeartbeatRequest req, EffectiveChildState effective, bool authenticated, Guid? deviceId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();

            // K4: Store a privacy-first screen time summary for parent reporting.
            _policiesByChildGuid.TryGetValue(key, out var policy);

            int? limit = policy?.DailyScreenTimeLimitMinutes;
            if (limit is not null && limit.Value > 0)
            {
                var extra = GetActiveExtraScreenTimeMinutesUnsafe_NoLock(childId, DateTimeOffset.UtcNow);
                if (extra > 0)
            {
                return false;
            }
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


    public ChildCommand CreateCommand(ChildId childId, CreateChildCommandRequest req)
{
    lock (_gate)
    {
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list))
        {
            list = new List<ChildCommand>();
            _commandsByChildGuid[key] = list;
        }

        DateTimeOffset? expires = null;
        if (req.ExpiresInMinutes is not null && req.ExpiresInMinutes.Value > 0)
        {
            expires = DateTimeOffset.UtcNow.AddMinutes(req.ExpiresInMinutes.Value);
        }

        var cmd = new ChildCommand(
            CommandId: Guid.NewGuid(),
            Type: req.Type,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            PayloadJson: req.PayloadJson,
            ExpiresAtUtc: expires);

        list.Add(cmd);

        EnsureChildProfileUnsafe_NoLock(childId);
        PersistUnsafe_NoLock();
        return cmd;
    }
}

public IReadOnlyList<ChildCommand> GetCommands(ChildId childId, int take = 50)
{
    lock (_gate)
    {
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list) || list.Count == 0)
        {
            return Array.Empty<ChildCommand>();
        }

        // newest first
        return list
            .OrderByDescending(c => c.CreatedAtUtc)
            .Take(Math.Max(1, take))
            .ToList();
    }
}

public IReadOnlyList<ChildCommand> GetPendingCommands(ChildId childId, int max = 20)
{
    lock (_gate)
    {
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list) || list.Count == 0)
        {
            return Array.Empty<ChildCommand>();
        }

