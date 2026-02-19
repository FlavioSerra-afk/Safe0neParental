using System.Security.Cryptography;\nusing System.Text.Json;\nusing System.Text.Json.Nodes;\nusing Safe0ne.Shared.Contracts;\n\nnamespace Safe0ne.DashboardServer.ControlPlane;\n

// AUTO-SPLIT CP01: Access Requests + grants (K8/P11).
// No behavior changes; purely a partial-by-domain split.

public sealed partial class JsonFileControlPlane
{
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
                Tamper: tamper);

            _statusByChildGuid[key] = status;

            EnsureChildProfileUnsafe_NoLock(childId);
            PersistUnsafe_NoLock();
            return status;
        }
    }

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
}
