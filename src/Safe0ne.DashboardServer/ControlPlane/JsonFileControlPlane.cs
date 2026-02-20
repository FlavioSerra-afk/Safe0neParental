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
        public sealed record LocalChildSnapshot(
        Guid Id,
        string DisplayName,
        string? Gender,
        string? AgeGroup,
        JsonElement? Avatar,
        bool IsArchived,
        DateTimeOffset? ArchivedAtUtc)
    {
        // Compatibility aliases used by older codepaths / endpoints.
        [System.Text.Json.Serialization.JsonIgnore]
        public bool Archived => IsArchived;

        [System.Text.Json.Serialization.JsonIgnore]
        public DateTimeOffset? ArchivedAt => ArchivedAtUtc;
    }

private const int CurrentSchemaVersion = 3;
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





    

    // Local Mode Activity members extracted to JsonFileControlPlane.Activity.cs (CP01-D).

/// <summary>
/// Get the last known location JSON for a child. Returns a stable JSON object even when no location is known.
/// </summary>

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

            DateTimeOffset? childLastSeen = null;
            if (_statusByChildGuid.TryGetValue(key, out var s))
            {
                childLastSeen = s.LastSeenUtc;
            }

            var now = DateTimeOffset.UtcNow;
            var singleDevice = devices.Count == 1;

            return devices
                .OrderByDescending(d => d.PairedAtUtc)
                .Select(d =>
                {
                    var expired = IsDeviceTokenExpired(now, d.TokenExpiresAtUtc);
                    var revoked = d.TokenRevokedAtUtc.HasValue;
                    var lastSeen = d.LastSeenUtc ?? (singleDevice ? childLastSeen : null);
                    return new ChildDeviceSummary(
                        DeviceId: d.DeviceId,
                        DeviceName: d.DeviceName,
                        AgentVersion: d.AgentVersion,
                        PairedAtUtc: d.PairedAtUtc,
                        LastSeenUtc: lastSeen,
                        TokenIssuedAtUtc: d.TokenIssuedAtUtc,
                        TokenExpiresAtUtc: d.TokenExpiresAtUtc,
                        TokenRevokedAtUtc: d.TokenRevokedAtUtc,
                        TokenRevokedBy: d.TokenRevokedBy,
                        TokenExpired: expired,
                        TokenRevoked: revoked);
                })
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

            var now = DateTimeOffset.UtcNow;
            if (match.TokenRevokedAtUtc.HasValue)
            {
                return false;
            }
            if (IsDeviceTokenExpired(now, match.TokenExpiresAtUtc))
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

            var issuedAt = DateTimeOffset.UtcNow;

            devices.Add(new PairedDevice(
                DeviceId: deviceId,
                DeviceName: deviceName,
                AgentVersion: agentVersion,
                PairedAtUtc: issuedAt,
                TokenHashSha256: tokenHash,
                LastSeenUtc: null,
                TokenIssuedAtUtc: issuedAt,
                TokenExpiresAtUtc: ComputeDeviceTokenExpiresAt(issuedAt),
                TokenRevokedAtUtc: null,
                TokenRevokedBy: null,
                TokenRevokedReason: null));

            // One-time code is consumed.
            _pendingPairingByChildGuid.Remove(key);

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();

            return new PairingCompleteResponse(childId, deviceId, token, issuedAt);
        }
    }

    public ChildAgentStatus UpsertStatus(ChildId childId, ChildAgentHeartbeatRequest req, EffectiveChildState effective, bool authenticated, Guid? deviceId)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            // Device health: record per-device last seen when authenticated.
            // Best-effort only; never throws.
            if (authenticated && deviceId.HasValue)
            {
                try
                {
                    if (_devicesByChildGuid.TryGetValue(key, out var devs) && devs is not null && devs.Count > 0)
                    {
                        var idx = devs.FindIndex(d => d.DeviceId == deviceId.Value);
                        if (idx >= 0)
                        {
                            var d = devs[idx];
                            devs[idx] = d with { LastSeenUtc = DateTimeOffset.UtcNow };
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }


            // K4: Store a privacy-first screen time summary for parent reporting.
            _policiesByChildGuid.TryGetValue(key, out var policy);

            int? limit = policy?.DailyScreenTimeLimitMinutes;
            if (limit is not null && limit.Value > 0)
            {
                var extra = GetActiveExtraScreenTimeMinutesUnsafe_NoLock(childId, DateTimeOffset.UtcNow);
                if (extra > 0)
                {
                    limit = limit.Value + extra;
                }
            }
            int? usedMins = null;
            int? remainingMins = null;
            bool depleted = false;

            if (req.ScreenTime is not null)
            {
                usedMins = Math.Max(0, req.ScreenTime.UsedSecondsToday / 60);
                if (limit is not null && limit.Value > 0)
                {
                    remainingMins = Math.Max(0, limit.Value - usedMins.Value);
                    depleted = usedMins.Value >= limit.Value || req.ScreenTime.BudgetDepleted;
                }
            }

            // K5: apps usage + blocked attempts rollups
            var blockedTotal = 0;
            BlockedAttemptItem[]? topBlocked = null;
            AppUsageItem[]? topUsage = null;
            if (req.Apps is not null)
            {
                blockedTotal = req.Apps.BlockedAttempts?.Sum(b => b.Count) ?? 0;
                topBlocked = req.Apps.BlockedAttempts;
                topUsage = req.Apps.Usage;
            }

            // K6: web rollups
            var webBlockedConfigured = req.Web?.BlockedDomainsConfigured ?? 0;
            var webTopBlocked = req.Web?.TopBlockedDomains;
            var webAlerts = req.Web?.AlertsToday ?? 0;
            var circumvention = req.Circumvention;
            var tamper = req.Tamper;

            // K10+: Gate device integrity signals and alert activity based on policy.
            // Defaults are permissive (true) for backward compatibility.
            var allowCircSignals = (policy?.WebCircumventionDetectionEnabled ?? true);
            var allowTamperSignals = (policy?.DeviceTamperDetectionEnabled ?? true);
            var allowCircAlerts = (policy?.WebCircumventionAlertsEnabled ?? true);
            var allowTamperAlerts = (policy?.DeviceTamperAlertsEnabled ?? true);

            if (!allowCircSignals || !allowCircAlerts) circumvention = null;
            if (!allowTamperSignals || !allowTamperAlerts) tamper = null;

            // Activity events are edge-triggered on signal transitions to reduce noise.
            try
            {
                var prev = _statusByChildGuid.TryGetValue(key, out var prior) ? prior : null;

                bool prevCirc = prev?.Circumvention is not null && (prev.Circumvention.VpnSuspected || prev.Circumvention.ProxyEnabled || prev.Circumvention.PublicDnsDetected || prev.Circumvention.HostsWriteFailed);
                bool nowCirc = circumvention is not null && (circumvention.VpnSuspected || circumvention.ProxyEnabled || circumvention.PublicDnsDetected || circumvention.HostsWriteFailed);

                bool prevTamper = prev?.Tamper is not null && (prev.Tamper.NotRunningElevated || prev.Tamper.EnforcementError);
                bool nowTamper = tamper is not null && (tamper.NotRunningElevated || tamper.EnforcementError);

                var evts = new System.Collections.Generic.List<object>();

                if (allowCircAlerts && nowCirc && !prevCirc)
                {
                    evts.Add(new { kind = "device_circumvention_detected", occurredAtUtc = DateTimeOffset.UtcNow.ToString("O"), vpnSuspected = circumvention!.VpnSuspected, proxyEnabled = circumvention.ProxyEnabled, publicDnsDetected = circumvention.PublicDnsDetected, hostsWriteFailed = circumvention.HostsWriteFailed });
                }
                if (allowTamperAlerts && nowTamper && !prevTamper)
                {
                    evts.Add(new { kind = "device_tamper_detected", occurredAtUtc = DateTimeOffset.UtcNow.ToString("O"), notRunningElevated = tamper!.NotRunningElevated, enforcementError = tamper.EnforcementError, lastError = tamper.LastError, lastErrorAtUtc = tamper.LastErrorAtUtc });
                }

                if (evts.Count > 0)
                {
                    var evtJson = JsonSerializer.Serialize(evts, JsonDefaults.Options);
                    AppendLocalActivityJsonUnsafe_NoLock(childId.Value.ToString(), evtJson);
                }
            }
            catch { }


            // 16W19/16W21: policy sync observability + watchdog (server-side best effort).
            var configuredPolicyVersion = effective.PolicyVersion.Value;
            var lastAppliedPolicyVersion = req.LastAppliedPolicyVersion;

            DateTimeOffset? pendingSinceUtc = null;
            var overdue = false;
            string? applyState = null;

            if (lastAppliedPolicyVersion is null || lastAppliedPolicyVersion.Value < configuredPolicyVersion)
            {
                pendingSinceUtc = policy?.UpdatedAtUtc ?? DateTimeOffset.UtcNow;
                var threshold = GetPolicyWatchdogThresholdUnsafe_NoLock();
                if (threshold <= TimeSpan.Zero)
                {
                    overdue = true;
                }
                else
                {
                    var age = DateTimeOffset.UtcNow - pendingSinceUtc.Value;
                    overdue = age >= threshold;
                }
                applyState = overdue ? "overdue" : "pending";
            }
            else
            {
                applyState = "in_sync";
            }

            // 16W19: policy apply failures -> emit activity edge-triggered.
            try
            {
                var prior = _statusByChildGuid.TryGetValue(key, out var p) ? p : null;
                if (req.LastPolicyApplyFailedAtUtc is not null)
                {
                    var prevFail = prior?.LastPolicyApplyFailedAtUtc;
                    if (prevFail is null || req.LastPolicyApplyFailedAtUtc > prevFail)
                    {
                        var evt = new[] { new { kind = "policy_apply_failed", occurredAtUtc = DateTimeOffset.UtcNow.ToString("O"), failedAtUtc = req.LastPolicyApplyFailedAtUtc?.ToString("O"), error = req.LastPolicyApplyError } };
                        var evtJson = System.Text.Json.JsonSerializer.Serialize(evt, JsonDefaults.Options);
                        AppendLocalActivityJsonUnsafe_NoLock(childId.Value.ToString(), evtJson);
                    }
                }
            }
            catch { }
            var status = new ChildAgentStatus(
                ChildId: childId,
                LastSeenUtc: DateTimeOffset.UtcNow,
                DeviceName: req.DeviceName,
                AgentVersion: req.AgentVersion,
                EffectiveMode: effective.EffectiveMode,
                ReasonCode: effective.ReasonCode,
                PolicyVersion: effective.PolicyVersion,
                Authenticated: authenticated,
                DeviceId: deviceId,
                ScreenTimeLimitMinutes: limit,
                ScreenTimeUsedMinutes: usedMins,
                ScreenTimeRemainingMinutes: remainingMins,
                ScreenTimeBudgetDepleted: depleted,
                ActiveSchedule: effective.ActiveSchedule,
                BlockedAttemptsToday: blockedTotal,
                TopBlockedApps: topBlocked,
                TopAppUsage: topUsage,
                WebBlockedDomainsConfigured: webBlockedConfigured,
                WebTopBlockedDomains: webTopBlocked,
                WebAlertsToday: webAlerts,
                Circumvention: circumvention,
                Tamper: tamper,
                LastAppliedPolicyVersion: req.LastAppliedPolicyVersion,
                LastAppliedPolicyEffectiveAtUtc: req.LastAppliedPolicyEffectiveAtUtc,
                LastAppliedPolicyFingerprint: req.LastAppliedPolicyFingerprint,
                LastPolicyApplyFailedAtUtc: req.LastPolicyApplyFailedAtUtc,
                LastPolicyApplyError: req.LastPolicyApplyError,
                PolicyApplyPendingSinceUtc: pendingSinceUtc,
                PolicyApplyOverdue: overdue,
                PolicyApplyState: applyState,
                LastKnownGoodPolicyVersion: req.LastKnownGoodPolicyVersion,
                RecommendedRollbackPolicyVersion: req.RecommendedRollbackPolicyVersion,
                RecommendedRollbackReason: req.RecommendedRollbackReason,
                RecommendedRollbackGeneratedAtUtc: req.RecommendedRollbackGeneratedAtUtc);

            _statusByChildGuid[key] = status;

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();
            return status;
        }
    }



    public bool TryGetLatestDiagnosticsBundle(ChildId childId, out DiagnosticsBundleInfo info)
    {
        lock (_gate)
        {
            return _diagnosticsByChildGuid.TryGetValue(childId.Value.ToString(), out info!);
        }
    }

    public DiagnosticsBundleInfo UpsertDiagnosticsBundle(ChildId childId, string fileName, long sizeBytes, DateTimeOffset createdAtUtc)
    {
        lock (_gate)
        {
            var info = new DiagnosticsBundleInfo(childId, createdAtUtc, sizeBytes, fileName);
            _diagnosticsByChildGuid[childId.Value.ToString()] = info;
            PersistUnsafe_NoLock();
            return info;
        }
    }

    public IReadOnlyList<Grant> GetActiveGrants(ChildId childId, DateTimeOffset nowUtc)
    {
        lock (_gate)
        {
            return _grants
                .Where(g => g.ChildId.Value == childId.Value)
                .Where(g => g.ExpiresAtUtc > nowUtc)
                .OrderByDescending(g => g.CreatedAtUtc)
                .ToList();
        }
    }

    // K9: Diagnostics bundle storage.
    public DiagnosticsBundleInfo SaveDiagnosticsBundle(ChildId childId, string fileName, Stream zipStream)
    {
        lock (_gate)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Safe0ne",
                "DashboardServer",
                "diagnostics",
                childId.Value.ToString());
            Directory.CreateDirectory(dir);

            var safeName = string.IsNullOrWhiteSpace(fileName) ? "bundle.zip" : Path.GetFileName(fileName);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss");
            var path = Path.Combine(dir, $"bundle_{stamp}_{safeName}");

            using (var fs = File.Create(path))
            {
                zipStream.CopyTo(fs);
            }

            var fi = new FileInfo(path);
            var info = new DiagnosticsBundleInfo(
                ChildId: childId,
                CreatedAtUtc: DateTimeOffset.UtcNow,
                SizeBytes: fi.Length,
                FileName: fi.Name);

            _diagnosticsByChildGuid[childId.Value.ToString()] = info;
            PersistUnsafe_NoLock();
            return info;
        }
    }

    public bool TryGetLatestDiagnosticsBundle(ChildId childId, out DiagnosticsBundleInfo info, out string fullPath)
    {
        lock (_gate)
        {
            if (!_diagnosticsByChildGuid.TryGetValue(childId.Value.ToString(), out info!))
            {
                fullPath = string.Empty;
                return false;
            }

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Safe0ne",
                "DashboardServer",
                "diagnostics",
                childId.Value.ToString());
            fullPath = Path.Combine(dir, info.FileName);
            if (!File.Exists(fullPath))
            {
                return false;
            }
            return true;
        }
    }

    public IReadOnlyList<DiagnosticsBundleInfo> ListDiagnosticsBundles(ChildId childId, int max = 25)
    {
        // Intentionally derived from the filesystem so we don't expand the persisted schema.
        // New bundles are written into the child diagnostics folder as bundle_<stamp>_<name>.
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "DashboardServer",
            "diagnostics",
            childId.Value.ToString());

        if (!Directory.Exists(dir))
        {
            return Array.Empty<DiagnosticsBundleInfo>();
        }

        var files = Directory
            .EnumerateFiles(dir, "bundle_*", SearchOption.TopDirectoryOnly)
            .Select(p => new FileInfo(p))
            .OrderByDescending(fi => fi.CreationTimeUtc)
            .Take(Math.Clamp(max, 1, 200))
            .ToList();

        var list = new List<DiagnosticsBundleInfo>(files.Count);
        foreach (var fi in files)
        {
            list.Add(new DiagnosticsBundleInfo(
                ChildId: childId,
                CreatedAtUtc: new DateTimeOffset(fi.CreationTimeUtc, TimeSpan.Zero),
                SizeBytes: fi.Length,
                FileName: fi.Name));
        }
        return list;
    }

    public bool TryGetDiagnosticsBundleByFileName(ChildId childId, string fileName, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(fileName)) return false;

        var safeName = Path.GetFileName(fileName);
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Safe0ne",
            "DashboardServer",
            "diagnostics",
            childId.Value.ToString());
        var path = Path.Combine(dir, safeName);
        if (!File.Exists(path)) return false;
        fullPath = path;
        return true;
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

        var now = DateTimeOffset.UtcNow;
        return list
            .Where(c => !c.Acked && (c.ExpiresAtUtc is null || c.ExpiresAtUtc > now))
            .OrderBy(c => c.CreatedAtUtc)
            .Take(Math.Max(1, max))
            .ToList();
    }
}

public bool TryAckCommand(ChildId childId, Guid commandId, Guid deviceId, AckChildCommandRequest req, out ChildCommand updated)
{
    lock (_gate)
    {
        updated = default!;
        var key = childId.Value.ToString();
        if (!_commandsByChildGuid.TryGetValue(key, out var list) || list.Count == 0)
        {
            return false;
        }

        var idx = list.FindIndex(c => c.CommandId == commandId);
        if (idx < 0) return false;

        var existing = list[idx];
        if (existing.Acked)
        {
            updated = existing;
            return true;
        }

        updated = existing with
        {
            Acked = true,
            AckedAtUtc = DateTimeOffset.UtcNow,
            AckedByDeviceId = deviceId,
            AckResult = req.Result,
            AckDetail = req.Detail
        };

        list[idx] = updated;

        PersistUnsafe_NoLock();
        return true;
    }
}

public ChildPolicy UpsertPolicy(ChildId childId, UpdateChildPolicyRequest req)
    {
        lock (_gate)
        {
            var key = childId.Value.ToString();
            if (!_policiesByChildGuid.TryGetValue(key, out var existing))
            {
                existing = new ChildPolicy(
                    ChildId: childId,
                    Version: PolicyVersion.Initial,
                    Mode: SafetyMode.Open,
                    UpdatedAtUtc: DateTimeOffset.UtcNow,
                    UpdatedBy: "system",
                    BlockedProcessNames: new[] { "notepad.exe" });
            }

            // Patch semantics: optional fields override when provided.
            var alwaysAllowed = req.AlwaysAllowed ?? existing.AlwaysAllowed;

            // Optional: block list is stored on the policy but only enforced for certain modes (agent-side).
            var blocked = req.BlockedProcessNames ?? existing.BlockedProcessNames;

            // K4/P6: screen time + schedules
            var dailyLimit = req.DailyScreenTimeLimitMinutes ?? existing.DailyScreenTimeLimitMinutes;
            var bedtime = req.BedtimeWindow ?? existing.BedtimeWindow;
            var school = req.SchoolWindow ?? existing.SchoolWindow;
            var homework = req.HomeworkWindow ?? existing.HomeworkWindow;

            // K5/P7: apps & games
            var appsAllowListEnabled = req.AppsAllowListEnabled ?? existing.AppsAllowListEnabled;
            var allowedApps = req.AllowedProcessNames ?? existing.AllowedProcessNames;
            var perAppLimits = req.PerAppDailyLimits ?? existing.PerAppDailyLimits;

            // K6/P8: web & content filtering
            var webAdult = req.WebAdultBlockEnabled ?? existing.WebAdultBlockEnabled;
            var webRules = req.WebCategoryRules ?? existing.WebCategoryRules;
            var webAllow = req.WebAllowedDomains ?? existing.WebAllowedDomains;
            var webBlock = req.WebBlockedDomains ?? existing.WebBlockedDomains;
            var webCirc = req.WebCircumventionDetectionEnabled ?? existing.WebCircumventionDetectionEnabled;
            var webSafe = req.WebSafeSearchEnabled ?? existing.WebSafeSearchEnabled;


            // K10+: device integrity signals gates
            var tamperDetect = req.DeviceTamperDetectionEnabled ?? existing.DeviceTamperDetectionEnabled;
            var tamperAlerts = req.DeviceTamperAlertsEnabled ?? existing.DeviceTamperAlertsEnabled;
            var circAlerts = req.WebCircumventionAlertsEnabled ?? existing.WebCircumventionAlertsEnabled;


            DateTimeOffset? grantUntil = existing.GrantUntilUtc;
            if (req.GrantMinutes is not null)
            {
                // GrantMinutes <= 0 clears the grant.
                grantUntil = req.GrantMinutes.Value <= 0
                    ? null
                    : DateTimeOffset.UtcNow.AddMinutes(req.GrantMinutes.Value);
            }

            var updated = existing with
            {
                Version = existing.Version.Next(),
                Mode = req.Mode,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedBy = req.UpdatedBy,
                AlwaysAllowed = alwaysAllowed,
                GrantUntilUtc = grantUntil,
                BlockedProcessNames = blocked,
                DailyScreenTimeLimitMinutes = dailyLimit,
                BedtimeWindow = bedtime,
                SchoolWindow = school,
                HomeworkWindow = homework,

                // K5/K6: apps + web config (persist the UI payload; enforcement is best-effort agent-side)
                AppsAllowListEnabled = appsAllowListEnabled,
                AllowedProcessNames = allowedApps,
                PerAppDailyLimits = perAppLimits,

                WebAdultBlockEnabled = webAdult,
                WebCategoryRules = webRules,
                WebAllowedDomains = webAllow,
                WebBlockedDomains = webBlock,
                WebCircumventionDetectionEnabled = webCirc,
                WebSafeSearchEnabled = webSafe,
                DeviceTamperDetectionEnabled = tamperDetect,
                DeviceTamperAlertsEnabled = tamperAlerts,
                WebCircumventionAlertsEnabled = circAlerts
            };

            _policiesByChildGuid[key] = updated;

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();
            return updated;
        }
    }

    // Persistence / serialization extracted to JsonFileControlPlane.Serialization.cs


}
