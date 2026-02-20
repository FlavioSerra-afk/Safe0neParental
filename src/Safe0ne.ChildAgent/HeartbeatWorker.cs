using System.Net.Http;
using System.Net.Http.Json;
using System.Linq;
using System.Text.Json;
using System.Diagnostics;
using System.Security.Principal;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Safe0ne.Shared.Contracts;
using Safe0ne.ChildAgent.ScreenTime;
using Safe0ne.ChildAgent.AppUsage;
using Safe0ne.ChildAgent.Activity;
using Safe0ne.ChildAgent.WebFilter;
using Safe0ne.ChildAgent.Pairing;
using Safe0ne.ChildAgent.ChildUx;
using Safe0ne.ChildAgent.Requests;
using Safe0ne.ChildAgent.Diagnostics;
using Safe0ne.ChildAgent.Location;
using Safe0ne.ChildAgent.Policy;

namespace Safe0ne.ChildAgent;

public sealed class HeartbeatWorker : BackgroundService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<HeartbeatWorker> _logger;
    private readonly EnrollmentService _enrollment;

    // K10: coarse tamper/health signals
    private DateTimeOffset? _lastEnforcementErrorAtUtc;
    private string? _lastEnforcementError;

    // K7/K8: throttle local UX navigation so we don't spam the child.
    private DateTimeOffset _lastUxNavigateUtc = DateTimeOffset.MinValue;
    private readonly ChildStateStore _childState;
    private readonly AccessRequestQueue _requests;
    private readonly LocationSender _location;

    private static readonly Guid DefaultChildGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public HeartbeatWorker(IHttpClientFactory httpClientFactory, ILogger<HeartbeatWorker> logger, EnrollmentService enrollment, ChildStateStore childState, AccessRequestQueue requests)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _enrollment = enrollment;
        _childState = childState;
        _requests = requests;
        _location = new LocationSender();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var childId = ResolveChildId();
        var deviceName = Environment.MachineName;
        var agentVersion = typeof(HeartbeatWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0";

        // Load auth for the resolved child. If missing, attempt canonical enroll-by-code (does not require knowing ChildId in advance).
        var auth = AgentAuthStore.LoadAuth(childId);
        if (auth is null)
        {
            var code = Environment.GetEnvironmentVariable("SAFEONE_PAIR_CODE");
            var deviceNameOverride = Environment.GetEnvironmentVariable("SAFEONE_DEVICE_NAME");

            if (!string.IsNullOrWhiteSpace(code))
            {
                var enroll = await _enrollment.EnrollByCodeAsync(code.Trim(), deviceNameOverride ?? deviceName, agentVersion, stoppingToken);
                if (enroll.Ok && enroll.Data is not null)
                {
                    // EnrollmentService already persisted auth and current child id; align local runtime state to the server-assigned child.
                    childId = enroll.Data.ChildId;
                    auth = AgentAuthStore.LoadAuth(childId);

                    // Stop repeated attempts.
                    Environment.SetEnvironmentVariable("SAFEONE_PAIR_CODE", null);
                }
                else
                {
                    _logger.LogWarning("Enroll-by-code failed: {Message}", enroll.Message);
                }
            }

            if (auth is null)
            {
                _logger.LogWarning("Not paired yet. Set SAFEONE_PAIR_CODE to pair this device.");
            }
        }

        // K4: daily screen time tracker (privacy-first, idle-filtered)
        var screenTracker = new ScreenTimeTracker(childId);
        // K5: privacy-first per-app aggregates (best effort: foreground app sampling)
        var appTracker = new AppUsageTracker(childId);
        // K6: web filtering (best effort)
        var webFilter = new WebFilterManager(childId);

        var idleThreshold = TimeSpan.FromSeconds(60);
        DateTimeOffset lastLockAttemptUtc = DateTimeOffset.MinValue;
        DateTimeOffset lastLocationPostUtc = DateTimeOffset.MinValue;

        bool lastDepleted = false;
        bool lastBedtimeActive = false;
        var warnedDynamicThresholds = new HashSet<int>();

	        // Slice #3: track UnblockApp grants (in-memory) so we can emit a lightweight "unblocked" activity once.
	        var lastUnblockApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        _logger.LogInformation("Safe0ne Child Agent started. ChildId={ChildId} Device={Device} Version={Version}", childId.Value, deviceName, agentVersion);


        // PATCH 10B: policy enforcement v1 (Local Mode)
        ActivityOutbox? activityOutbox = auth is not null ? new ActivityOutbox(childId) : null;
        string? lastAppliedPolicyFingerprint = null;
        int? lastAppliedPolicyVersion = null;
        DateTimeOffset? lastAppliedPolicyEffectiveAtUtc = null;
        // 16W19: best-effort policy apply failure signals (used for parent-side observability).
        // These are additive-only and safe when the server/parent doesn't yet consume them.
        DateTimeOffset? lastPolicyApplyFailedAtUtc = null;
        string? lastPolicyApplyError = null;
        bool usingCachedPolicy = false;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("ControlPlane");

				
                // Phase A: keep the policy surface variable declared early so later code can safely reference it.
                JsonElement? policySurface = null;
if (auth?.DeviceToken is not null)
                {
                    client.DefaultRequestHeaders.Remove(AgentAuth.DeviceTokenHeaderName);
                    client.DefaultRequestHeaders.Add(AgentAuth.DeviceTokenHeaderName, auth.DeviceToken);
                }

                // K8: best-effort flush queued access requests + sync their statuses.
                // This is safe even if not paired/offline.
                await _requests.FlushAndSyncAsync(client, childId, stoppingToken);

                // PATCH 10B: best-effort flush activity events (policy apply, enforcement notices, etc.)
                if (activityOutbox is not null)
                {
                    await activityOutbox.FlushAsync(client, childId, stoppingToken);
                }

                // K11 (Local Mode scaffold): best-effort post last-known location at low cadence.
                // Privacy-first: until real geo capture exists, we may post "unavailable" or a test/fake coordinate.
                var nowUtcLoc = DateTimeOffset.UtcNow;
                if ((nowUtcLoc - lastLocationPostUtc) >= TimeSpan.FromMinutes(1))
                {
                    lastLocationPostUtc = nowUtcLoc;
                    await _location.PostLastKnownAsync(client, childId, deviceName, nowUtcLoc, stoppingToken);
                }

                // Pull effective state (optional in Local Mode; keep agent robust if endpoint missing).
                EffectiveChildState? effective = null;
                var effRes = await TryGetApiResponseAsync<EffectiveChildState>(
                    client,
                    $"/api/{ApiVersions.V1}/children/{childId.Value}/effective",
                    stoppingToken);

                if (effRes?.Ok == true)
                {
                    effective = effRes.Data;
                }

                // PATCH 10B: Local Mode policy enforcement v1.
                // Prefer the Local Mode policy envelope endpoint (Phase A) for stable versioning.
                ChildPolicy? policy = null;

                int? policyVersion = null;
                DateTimeOffset? policyEffectiveAtUtc = null;
				// policySurface declared above

                var localPolicyEnvRes = await TryGetApiResponseAsync<JsonElement>(
                    client,
                    $"/api/local/children/{childId.Value}/policy",
                    stoppingToken);

                if (localPolicyEnvRes?.Ok == true && localPolicyEnvRes.Data.ValueKind == JsonValueKind.Object)
                {
                    if (localPolicyEnvRes.Data.TryGetProperty("policyVersion", out var pv) && pv.ValueKind == JsonValueKind.Number && pv.TryGetInt32(out var pvi))
                        policyVersion = pvi;
                    if (localPolicyEnvRes.Data.TryGetProperty("effectiveAtUtc", out var ea) && ea.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(ea.GetString(), out var dto))
                        policyEffectiveAtUtc = dto;
                    if (localPolicyEnvRes.Data.TryGetProperty("policy", out var pol) && pol.ValueKind == JsonValueKind.Object)
                        policySurface = pol;
                }


                // 16W14: replay protection. Ignore older policy versions than what we already applied.
                // This prevents a stale local server state (or a reboot) from rolling the kid back to an older policy.
                if (policyVersion is not null && lastAppliedPolicyVersion is not null && policyVersion.Value < lastAppliedPolicyVersion.Value)
                {
                    var incoming = policyVersion;

                    // Treat as unavailable so we fall back to cache/legacy.
                    policySurface = null;
                    policyEffectiveAtUtc = null;
                    policyVersion = null;

                    try
                    {
                        activityOutbox ??= new ActivityOutbox(childId);
                        activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                            EventId: Guid.NewGuid(),
                            OccurredAtUtc: DateTimeOffset.UtcNow,
                            Kind: "policy_replay_ignored",
                            App: null,
                            Details: JsonSerializer.Serialize(new { incomingVersion = incoming, lastAppliedVersion = lastAppliedPolicyVersion, note = "Ignored policy replay (older version)" }, JsonDefaults.Options),
                            DeviceId: auth?.DeviceId.ToString()));
                    }
                    catch { }
                }

	                // 16X: Offline-first guardrail.
	                // Load last cached policy once (best-effort) so we can keep enforcing if the local server is unavailable.
	                var cached = PolicyCacheStore.Load(childId);

	                // If we did not obtain an expanded surface this tick, use the cached surface (if any).
	                if (policySurface is null && cached?.PolicySurface is { } cachedSurface && cachedSurface.ValueKind == JsonValueKind.Object)
	                {
	                    policySurface = cachedSurface;
	                    policyVersion ??= cached.PolicyVersion;
	                    policyEffectiveAtUtc ??= cached.EffectiveAtUtc;
	                }

	                var localProfileRes = await TryGetApiResponseAsync<JsonElement>(
	                    client,
	                    $"/api/local/children/{childId.Value}/profile",
	                    stoppingToken);

	                if (localProfileRes?.Ok == true && localProfileRes.Data.ValueKind != JsonValueKind.Undefined && localProfileRes.Data.ValueKind != JsonValueKind.Null)
	                {
	                    policy = TryMapPolicyFromLocalProfileJson(localProfileRes.Data, childId);
	                }

	                // If mapping failed (or local profile is unavailable), fall back to cached v1 policy.
	                if (policy is null && cached?.PolicyV1 is not null)
	                {
	                    policy = cached.PolicyV1;
	                    policyVersion ??= cached.PolicyVersion;
	                    policyEffectiveAtUtc ??= cached.EffectiveAtUtc;
	                }

	                // Legacy fallback (safe no-op if endpoint doesn't exist)
	                // Only attempt when we still don't have a v1 policy, so we prefer Local Mode.
	                bool legacyFetched = false;
	                if (policy is null)
	                {
	                    var polRes = await TryGetApiResponseAsync<ChildPolicy>(
	                        client,
	                        $"/api/{ApiVersions.V1}/children/{childId.Value}/policy",
	                        stoppingToken);

	                    if (polRes?.Ok == true)
	                    {
	                        policy = polRes.Data;
	                        legacyFetched = true;
	                    }
	                }

	                // Determine if we are actively relying on cached policy for enforcement.
	                // (No local policy envelope + no local profile + we didn't fetch legacy this tick.)
	                bool usedCacheThisTick = (cached is not null)
	                    && (localPolicyEnvRes?.Ok != true)
	                    && (localProfileRes?.Ok != true)
	                    && !legacyFetched
	                    && (policy is not null || policySurface is not null);

	                // Write-through cache whenever we have a usable policy surface (live or legacy).
	                if (policy is not null || policySurface is not null)
	                {
	                    PolicyCacheStore.Save(childId, new PolicyCacheStore.PolicyCacheEntry(
	                        CachedAtUtc: DateTimeOffset.UtcNow,
	                        PolicyVersion: policyVersion,
	                        EffectiveAtUtc: policyEffectiveAtUtc,
	                        PolicySurface: policySurface,
	                        PolicyV1: policy));
	                }

	                // Emit a one-shot activity event when we switch into/out of cached policy mode.
	                if (usedCacheThisTick && !usingCachedPolicy)
	                {
	                    usingCachedPolicy = true;
	                    activityOutbox ??= new ActivityOutbox(childId);
	                    activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
	                        EventId: Guid.NewGuid(),
	                        OccurredAtUtc: DateTimeOffset.UtcNow,
	                        Kind: "policy_cache_used",
	                        App: null,
	                        Details: JsonSerializer.Serialize(new
	                        {
	                            cachedAtUtc = cached!.CachedAtUtc,
	                            policyVersion = cached.PolicyVersion,
	                            effectiveAtUtc = cached.EffectiveAtUtc,
	                            note = "Using last cached policy (offline-first fallback)"
	                        }, JsonDefaults.Options),
	                        DeviceId: auth?.DeviceId.ToString()));
	                }
	                else if (!usedCacheThisTick && usingCachedPolicy)
	                {
	                    usingCachedPolicy = false;
	                }

                // Record policy application events into Activity (Kid -> Local SSOT).
                // Best-effort: only when we can compute a stable fingerprint.
                try
                {
                    if (policy is not null)
                    {
                        var fp = ComputePolicyFingerprint(policy);
                        if (!string.Equals(fp, lastAppliedPolicyFingerprint, StringComparison.Ordinal))
                        {
                            lastAppliedPolicyFingerprint = fp;

                            // Also track versioning if available (Phase A envelope).
                            if (policyVersion is not null) lastAppliedPolicyVersion = policyVersion;
                            if (policyEffectiveAtUtc is not null) lastAppliedPolicyEffectiveAtUtc = policyEffectiveAtUtc;

                            activityOutbox ??= new ActivityOutbox(childId);
                            activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                                EventId: Guid.NewGuid(),
                                OccurredAtUtc: DateTimeOffset.UtcNow,
                                Kind: "policy_applied",
                                App: null,
                                Details: BuildPolicyAppliedDetails(policy, policyVersion, policyEffectiveAtUtc, policySurface, source: usingCachedPolicy ? "cache" : "live"),
                                DeviceId: auth?.DeviceId.ToString()));

                            // Phase A: "observable enforcement" scaffolding.
                            // Emit "would enforce" events once per policy application.
                            try
                            {
                                var modeResolved = ResolveModeWithPrecedence(policySurface, policy?.Mode);
                                EmitWouldEnforceEvents(activityOutbox, auth?.DeviceId.ToString(), modeResolved, policySurface, policy);
                            }
                            catch
                            {
                                // ignore (best-effort)
                            }
                        }
                        else if (policyVersion is not null && policyVersion != lastAppliedPolicyVersion)
                        {
                            // Version changed without changing the v1 fingerprint (e.g., expanded policy surface).
                            lastAppliedPolicyVersion = policyVersion;
                            if (policyEffectiveAtUtc is not null) lastAppliedPolicyEffectiveAtUtc = policyEffectiveAtUtc;

                            activityOutbox ??= new ActivityOutbox(childId);
                            activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                                EventId: Guid.NewGuid(),
                                OccurredAtUtc: DateTimeOffset.UtcNow,
                                Kind: "policy_applied",
                                App: null,
                                Details: BuildPolicyAppliedDetails(policy, policyVersion, policyEffectiveAtUtc, policySurface, source: usingCachedPolicy ? "cache" : "live"),
                                DeviceId: auth?.DeviceId.ToString()));

                            try
                            {
                                var modeResolved = ResolveModeWithPrecedence(policySurface, policy?.Mode);
                                EmitWouldEnforceEvents(activityOutbox, auth?.DeviceId.ToString(), modeResolved, policySurface, policy);
                            }
                            catch { }
                        }
                    }
                }
                catch
                {
                    // ignore (best-effort)
                }

                // K4: tick screen time + decide whether budget is depleted
                var nowUtc = DateTimeOffset.UtcNow;
                var idle = Win32.GetIdleTime();
                var isActive = idle <= idleThreshold;

                string? foregroundExe = null;
                if (isActive)
                {
                    var pid = Win32.TryGetForegroundProcessId();
                    if (pid is not null)
                    {
                        try
                        {
                            var proc = Process.GetProcessById(pid.Value);
                            // ProcessName excludes .exe; normalize to exe for policy matching.
                            foregroundExe = (proc.ProcessName ?? string.Empty).Trim() + ".exe";
                        }
                        catch { }
                    }
                }

                
                var tick = screenTracker.Tick(nowUtc, idle, idleThreshold);
                appTracker.Tick(nowUtc, isActive, foregroundExe);

                // K4/K7: Screen time budget evaluation (Phase B, Vertical Slice #1)
                // Prefer expanded SSOT surface: policy.timeBudget.dailyMinutes + schedules.*.
                var nowLocal = DateTimeOffset.Now;
                var (dailyLimitMinutes, bedtimeWindow, schoolWindow, homeworkWindow) = ReadTimeBudgetFromPolicySurface(policySurface, policy, nowLocal);

                // PATCH 16R: grace period + warning thresholds (additive; defaults are conservative)
                var graceMinutes = 0;
                var warnAtMinutes = new List<int> { 5, 1 };
                try
                {
                    if (policySurface is { } ps && ps.ValueKind == JsonValueKind.Object && ps.TryGetProperty("timeBudget", out var tb2) && tb2.ValueKind == JsonValueKind.Object)
                    {
                        if (tb2.TryGetProperty("graceMinutes", out var gm))
                        {
                            if (gm.ValueKind == JsonValueKind.Number && gm.TryGetInt32(out var v)) graceMinutes = Math.Clamp(v, 0, 120);
                            else if (gm.ValueKind == JsonValueKind.String && int.TryParse(gm.GetString(), out var v2)) graceMinutes = Math.Clamp(v2, 0, 120);
                        }

                        // Prefer warnAtMinutes, but accept legacy warnMinutesRemaining.
                        JsonElement listEl;
                        if (tb2.TryGetProperty("warnAtMinutes", out listEl) || tb2.TryGetProperty("warnMinutesRemaining", out listEl))
                        {
                            if (listEl.ValueKind == JsonValueKind.Array)
                            {
                                var tmp = new List<int>();
                                foreach (var item in listEl.EnumerateArray())
                                {
                                    if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var m)) tmp.Add(Math.Clamp(m, 0, 240));
                                    else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var ms)) tmp.Add(Math.Clamp(ms, 0, 240));
                                }
                                // De-dupe + stable ordering (descending)
                                warnAtMinutes = tmp.Distinct().OrderByDescending(x => x).ToList();
                                if (warnAtMinutes.Count == 0) warnAtMinutes = new List<int> { 5, 1 };
                            }
                        }
                    }
                }
                catch { /* best-effort */ }

                int? limitMins = dailyLimitMinutes ?? policy?.DailyScreenTimeLimitMinutes;

                // K8/P11: approved grants can add extra minutes on top of the daily budget.
                var extraMins = ComputeExtraScreenTimeMinutes(effective?.ActiveGrants);
                if (extraMins > 0 && limitMins is not null && limitMins.Value > 0)
                {
                    limitMins = limitMins.Value + extraMins;
                }
                // Evaluate daily budget depletion (PATCH 16R: grace extends hard stop)
                bool depleted = false;
                int? remainingSec = null;
                if (limitMins is not null && limitMins.Value > 0)
                {
                var limitSec = limitMins.Value * 60;
                var graceSec = Math.Max(0, graceMinutes) * 60;
                // Remaining until hard stop (limit + grace)
                remainingSec = Math.Max(0, (limitSec + graceSec) - tick.UsedSecondsToday);
                depleted = remainingSec == 0;

                // Remaining until *limit* (warnings happen before grace kicks in)
                var remainingToLimitSec = Math.Max(0, limitSec - tick.UsedSecondsToday);

                // Threshold warnings -> Activity (best-effort)
                // Keep existing marker kinds for 5m/1m to avoid regressions.
                if (warnAtMinutes.Contains(5) && !tick.Warned5 && remainingToLimitSec <= 300 && remainingToLimitSec > 60)
                {
                    _logger.LogInformation("Screen time warning: 5 minutes remaining.");
                    screenTracker.MarkWarned5();
                    // 16W26: show a best-effort local warning screen (throttled).
                    ChildUx.ChildUxNavigation.TryOpenWarning("time", 5, ref _lastUxNavigateUtc, _logger);
                    activityOutbox ??= new ActivityOutbox(childId);
                    activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                        EventId: Guid.NewGuid(),
                        OccurredAtUtc: nowUtc,
                        Kind: "screen_time_warning_5m",
                        App: null,
                        Details: JsonSerializer.Serialize(new { remainingSeconds = remainingToLimitSec, usedSecondsToday = tick.UsedSecondsToday, dailyLimitMinutes = limitMins, graceMinutes, extraMinutes = extraMins }, JsonDefaults.Options),
                        DeviceId: auth?.DeviceId.ToString()));
                }
                if (warnAtMinutes.Contains(1) && !tick.Warned1 && remainingToLimitSec <= 60 && remainingToLimitSec > 0)
                {
                    _logger.LogInformation("Screen time warning: 1 minute remaining.");
                    screenTracker.MarkWarned1();
                    // 16W26: show a best-effort local warning screen (throttled).
                    ChildUx.ChildUxNavigation.TryOpenWarning("time", 1, ref _lastUxNavigateUtc, _logger);
                    activityOutbox ??= new ActivityOutbox(childId);
                    activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                        EventId: Guid.NewGuid(),
                        OccurredAtUtc: nowUtc,
                        Kind: "screen_time_warning_1m",
                        App: null,
                        Details: JsonSerializer.Serialize(new { remainingSeconds = remainingToLimitSec, usedSecondsToday = tick.UsedSecondsToday, dailyLimitMinutes = limitMins, graceMinutes, extraMinutes = extraMins }, JsonDefaults.Options),
                        DeviceId: auth?.DeviceId.ToString()));
                }

                // Additional thresholds (additive) beyond the built-in 5m/1m markers.
                // De-dupe is per-process-run to avoid spamming.
                foreach (var m in warnAtMinutes)
                {
                    if (m <= 0 || m == 1 || m == 5) continue;
                    if (warnedDynamicThresholds.Contains(m)) continue;
                    if (remainingToLimitSec <= (m * 60) && remainingToLimitSec > 0)
                    {
                        warnedDynamicThresholds.Add(m);
                        activityOutbox ??= new ActivityOutbox(childId);
                        activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                            EventId: Guid.NewGuid(),
                            OccurredAtUtc: nowUtc,
                            Kind: $"screen_time_warning_{m}m",
                            App: null,
                            Details: JsonSerializer.Serialize(new { thresholdMinutes = m, remainingSeconds = remainingToLimitSec, usedSecondsToday = tick.UsedSecondsToday, dailyLimitMinutes = limitMins, graceMinutes, extraMinutes = extraMins }, JsonDefaults.Options),
                            DeviceId: auth?.DeviceId.ToString()));
                    }
                }
                }

                // Evaluate bedtime schedule (forces restrictive mode while active)
                var bedtimeActive = IsWindowActive(nowLocal, bedtimeWindow);

                // Decide enforced mode:
                // - Respect effective mode if present (server-side decisions)
                // - Bedtime schedule forces Bedtime mode while active
                // - Budget depletion forces Lockdown
                var enforcedMode = effective?.EffectiveMode ?? policy?.Mode ?? SafetyMode.Open;
                if (bedtimeActive)
                {
                enforcedMode = SafetyMode.Bedtime;
                }
                if (depleted)
                {
                enforcedMode = SafetyMode.Lockdown;
                }

                // Transition events (best-effort) so Parent can observe the slice end-to-end.
                // De-dupe in-memory per agent run; tracker already de-dupes warning thresholds.
                if (depleted && !lastDepleted)
                {
                    lastDepleted = true;
                    activityOutbox ??= new ActivityOutbox(childId);
                    activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                        EventId: Guid.NewGuid(),
                        OccurredAtUtc: nowUtc,
                        Kind: "screen_time_depleted",
                        App: foregroundExe,
                        Details: JsonSerializer.Serialize(new { usedSecondsToday = tick.UsedSecondsToday, dailyLimitMinutes = limitMins, extraMinutes = extraMins, reason = "daily_budget" }, JsonDefaults.Options),
                        DeviceId: auth?.DeviceId.ToString()));
                }
                else if (!depleted && lastDepleted)
                {
                    lastDepleted = false;
                    activityOutbox ??= new ActivityOutbox(childId);
                    activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                        EventId: Guid.NewGuid(),
                        OccurredAtUtc: nowUtc,
                        Kind: "screen_time_restored",
                        App: null,
                        Details: JsonSerializer.Serialize(new { remainingSeconds = remainingSec, dailyLimitMinutes = limitMins, extraMinutes = extraMins }, JsonDefaults.Options),
                        DeviceId: auth?.DeviceId.ToString()));
                }

                if (bedtimeActive && !lastBedtimeActive)
                {
                    lastBedtimeActive = true;
                    activityOutbox ??= new ActivityOutbox(childId);
                    activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                        EventId: Guid.NewGuid(),
                        OccurredAtUtc: nowUtc,
                        Kind: "schedule_bedtime_started",
                        App: null,
                        Details: JsonSerializer.Serialize(new { startLocal = bedtimeWindow?.StartLocal, endLocal = bedtimeWindow?.EndLocal }, JsonDefaults.Options),
                        DeviceId: auth?.DeviceId.ToString()));
                }
                else if (!bedtimeActive && lastBedtimeActive)
                {
                    lastBedtimeActive = false;
                    activityOutbox ??= new ActivityOutbox(childId);
                    activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                        EventId: Guid.NewGuid(),
                        OccurredAtUtc: nowUtc,
                        Kind: "schedule_bedtime_ended",
                        App: null,
                        Details: JsonSerializer.Serialize(new { }, JsonDefaults.Options),
                        DeviceId: auth?.DeviceId.ToString()));
                }
// K6/K8E: web filtering applies hosts rules and emits privacy-first aggregates.
                // Active grants can temporarily allow a blocked domain.
                var policyForWeb = ApplyUnblockSiteGrants(policy, effective?.ActiveGrants);
                var (webReport, circ) = webFilter.TickAndApply(nowUtc, policyForWeb, isActive);

	                // Slice #3: UnblockApp grants should be observable. Emit a best-effort activity when a new
	                // UnblockApp target appears. Enforcement itself is handled inside TryEnforce via BuildUnblockAppSet().
	                try
	                {
	                    var currentUnblockApps = BuildUnblockAppSet(effective?.ActiveGrants);
	                    foreach (var exe in currentUnblockApps)
	                    {
	                        if (lastUnblockApps.Contains(exe)) continue;
	                        lastUnblockApps.Add(exe);

	                        activityOutbox ??= new ActivityOutbox(childId);
	                        activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
	                            EventId: Guid.NewGuid(),
	                            OccurredAtUtc: nowUtc,
	                            Kind: "app_unblocked",
	                            App: exe,
	                            Details: JsonSerializer.Serialize(new { reason = "grant_unblock_app" }, JsonDefaults.Options),
	                            DeviceId: auth?.DeviceId.ToString()));
	                    }
	                }
	                catch
	                {
	                    // ignore (best-effort)
	                }


			        // K4/K5: enforce (best-effort)
			        TryEnforce(enforcedMode, policy, depleted, bedtimeActive, foregroundExe, appTracker, effective?.ActiveGrants, ref lastLockAttemptUtc, ref _lastUxNavigateUtc);

                // K7: publish a lightweight snapshot for the local child UX.
                var usedTs = TimeSpan.FromSeconds(Math.Max(0, tick.UsedSecondsToday));
                var remTs = TimeSpan.FromSeconds(Math.Max(0, remainingSec ?? 0));
                var stSnapshot = new ScreenTimeSnapshot(usedTs, remTs, limitMins is not null && limitMins.Value > 0);
                _childState.Update(effective, policy, stSnapshot);

                var st = new ScreenTimeReport(
                    LocalDate: tick.LocalDate,
                    UsedSecondsToday: tick.UsedSecondsToday,
                    DailyLimitMinutes: limitMins,
                    BudgetDepleted: depleted);


                var tamper = new TamperSignals(
                    NotRunningElevated: OperatingSystem.IsWindows() && !IsRunningElevatedWindows(),
                    EnforcementError: _lastEnforcementErrorAtUtc is not null && (nowUtc - _lastEnforcementErrorAtUtc.Value) < TimeSpan.FromMinutes(30),
                    LastError: _lastEnforcementError,
                    LastErrorAtUtc: _lastEnforcementErrorAtUtc);
                // Build the strongly-typed heartbeat envelope (contracts are additive-only).
                // 16W19: also report the last policy we actually applied, and any best-effort apply failure signal.

                // If we recently hit an enforcement error, emit an activity marker (edge-triggered) and
                // surface it in the heartbeat for parent-side visibility.
                if (_lastEnforcementErrorAtUtc is not null && (nowUtc - _lastEnforcementErrorAtUtc.Value) < TimeSpan.FromMinutes(30))
                {
                    if (lastPolicyApplyFailedAtUtc is null || _lastEnforcementErrorAtUtc.Value > lastPolicyApplyFailedAtUtc.Value)
                    {
                        lastPolicyApplyFailedAtUtc = _lastEnforcementErrorAtUtc;
                        lastPolicyApplyError = _lastEnforcementError;

                        try
                        {
                            activityOutbox ??= new ActivityOutbox(childId);
                            activityOutbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                                EventId: Guid.NewGuid(),
                                OccurredAtUtc: DateTimeOffset.UtcNow,
                                Kind: "policy_apply_failed",
                                App: null,
                                Details: JsonSerializer.Serialize(new
                                {
                                    lastAppliedPolicyVersion,
                                    lastAppliedPolicyEffectiveAtUtc,
                                    lastAppliedPolicyFingerprint,
                                    error = _lastEnforcementError,
                                    note = "Best-effort signal: enforcement/apply error observed"
                                }, JsonDefaults.Options),
                                DeviceId: auth?.DeviceId.ToString()));
                        }
                        catch { }
                    }
                }

                var hb = new ChildAgentHeartbeatRequest(
                    DeviceName: deviceName,
                    AgentVersion: agentVersion,
                    SentAtUtc: nowUtc,
                    EffectiveState: effective,
                    ScreenTime: st,
                    Apps: appTracker.BuildReport(),
                    Web: webReport,
                    Circumvention: circ,
                    Tamper: tamper,
                    LastAppliedPolicyVersion: lastAppliedPolicyVersion,
                    LastAppliedPolicyEffectiveAtUtc: lastAppliedPolicyEffectiveAtUtc,
                    LastAppliedPolicyFingerprint: lastAppliedPolicyFingerprint,
                    LastPolicyApplyFailedAtUtc: lastPolicyApplyFailedAtUtc,
                    LastPolicyApplyError: lastPolicyApplyError);

                using var resp = await client.PostAsJsonAsync(
                    $"/api/{ApiVersions.V1}/children/{childId.Value}/heartbeat",
                    hb,
                    JsonDefaults.Options,
                    stoppingToken);

                if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogWarning("Heartbeat rejected (401). This child has paired devices; run pairing (SAFEONE_PAIR_CODE). ");
                }
                else if (!resp.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Heartbeat failed: HTTP {Status}", (int)resp.StatusCode);
                }
                else
                {
                    _logger.LogDebug("Heartbeat ok");
                }
            

                await PollAndAckCommandsAsync(client, childId, stoppingToken);
}
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Heartbeat loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task PollAndAckCommandsAsync(HttpClient client, ChildId childId, CancellationToken ct)
{
    try
    {
        var pending = await client.GetFromJsonAsync<ApiResponse<List<ChildCommand>>>(
            $"/api/{ApiVersions.V1}/children/{childId.Value}/commands/pending?max=10",
            JsonDefaults.Options,
            ct);

        if (pending?.Ok != true || pending.Data is null || pending.Data.Count == 0)
        {
            return;
        }

        foreach (var cmd in pending.Data)
        {
            var result = await ExecuteCommandAsync(cmd, client, childId, typeof(HeartbeatWorker).Assembly.GetName().Version?.ToString() ?? "0.0.0", ct);

            var ack = new AckChildCommandRequest(Result: result.Result, Detail: result.Detail);

            using var resp = await client.PostAsJsonAsync(
                $"/api/{ApiVersions.V1}/children/{childId.Value}/commands/{cmd.CommandId}/ack",
                ack,
                JsonDefaults.Options,
                ct);

            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("Command ack failed: CmdId={CmdId} HTTP {Status}", cmd.CommandId, (int)resp.StatusCode);
            }
        }
    }
    catch (Exception ex)
    {
        _logger.LogDebug(ex, "Command poll error");
    }
}

private async Task<(string Result, string? Detail)> ExecuteCommandAsync(ChildCommand cmd, HttpClient client, ChildId childId, string agentVersion, CancellationToken ct)
{
    // K2: No enforcement yet. We log + acknowledge.
    try
    {
        if (string.Equals(cmd.Type, CommandTypes.Notice, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("NOTICE command received: {Payload}", cmd.PayloadJson ?? "(no payload)");
            return ("ok", null);
        }

        if (string.Equals(cmd.Type, CommandTypes.Sync, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("SYNC command received");
            return ("ok", null);
        }

        if (string.Equals(cmd.Type, CommandTypes.Ping, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("PING command received");
            return ("ok", null);
        }

        

        if (string.Equals(cmd.Type, CommandTypes.DiagnosticsBundle, StringComparison.OrdinalIgnoreCase))
        {
            // Build and upload a privacy-first diagnostics bundle.
            var (zipBytes, fileName) = DiagnosticsBundle.BuildZip(childId, agentVersion);

            using var content = new ByteArrayContent(zipBytes);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

            using var uploadReq = new HttpRequestMessage(HttpMethod.Put, $"/api/{ApiVersions.V1}/children/{childId.Value}/diagnostics/bundles");
            uploadReq.Content = content;
            uploadReq.Headers.Add("X-Safe0ne-Diagnostics-FileName", fileName);

            using var uploadResp = await client.SendAsync(uploadReq, ct);
            if (!uploadResp.IsSuccessStatusCode)
            {
                return ("error", $"upload_failed_http_{(int)uploadResp.StatusCode}");
            }

            return ("ok", fileName);
        }

_logger.LogInformation("Unknown command type: {Type}", cmd.Type);
        return ("ignored", "unknown_command");
    }
    catch (Exception ex)
    {
        _logger.LogWarning(ex, "Command execution error: {CmdId}", cmd.CommandId);
        return ("error", ex.Message);
    }
    finally
    {
        await Task.CompletedTask;
    }
}

private static ChildId ResolveChildId()
    {
        // Env var override: SAFEONE_CHILD_ID must be a GUID.
        var raw = Environment.GetEnvironmentVariable("SAFEONE_CHILD_ID");
        if (!string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out var g))
        {
            return new ChildId(g);
        }
        var persisted = AgentAuthStore.LoadCurrentChildId();
        if (persisted.HasValue) return persisted.Value;
        return new ChildId(DefaultChildGuid);
    }

    
    // LEGACY-COMPAT: Older pairing flow that required knowing ChildId and called /api/v1/children/{childId}/pair/complete.
    // Canonical pairing is EnrollmentService.EnrollByCodeAsync (POST /api/local/devices/enroll).
    private async Task<AgentAuthStore.AgentAuthState?> TryPairAsync(ChildId childId, string deviceName, string agentVersion, CancellationToken ct)
    {
        var code = Environment.GetEnvironmentVariable("SAFEONE_PAIR_CODE");
        if (string.IsNullOrWhiteSpace(code)) return null;

        try
        {
            var client = _httpClientFactory.CreateClient("ControlPlane");
            var req = new PairingCompleteRequest(code.Trim(), deviceName, agentVersion);

            using var resp = await client.PostAsJsonAsync(
                $"/api/{ApiVersions.V1}/children/{childId.Value}/pair/complete",
                req,
                JsonDefaults.Options,
                ct);

            var parsed = await resp.Content.ReadFromJsonAsync<ApiResponse<PairingCompleteResponse>>(JsonDefaults.Options, ct);
            if (parsed?.Ok != true || parsed.Data is null)
            {
                _logger.LogWarning("Pairing failed: {Message}", parsed?.Error?.Message ?? $"HTTP {(int)resp.StatusCode}");
                return null;
            }

            return new AgentAuthStore.AgentAuthState(parsed.Data.DeviceId, parsed.Data.DeviceToken, parsed.Data.IssuedAtUtc);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pairing attempt error");
            return null;
        }
    }

    private static bool IsRunningElevatedWindows()
    {
        try
        {
            if (!OperatingSystem.IsWindows()) return false;
            using var id = WindowsIdentity.GetCurrent();
            var p = new WindowsPrincipal(id);
            return p.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    // PATCH 10B helpers: robust Local API reads + mapping local SSOT profile -> ChildPolicy.
    private static async Task<ApiResponse<T>?> TryGetApiResponseAsync<T>(HttpClient client, string url, CancellationToken ct)
    {
        try
        {
            using var resp = await client.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ApiResponse<T>>(JsonDefaults.Options, ct);
        }
        catch
        {
            return null;
        }
    }

    
private static (int? DailyLimitMinutes, ScheduleWindow? Bedtime, ScheduleWindow? School, ScheduleWindow? Homework) ReadTimeBudgetFromPolicySurface(JsonElement? policySurface, ChildPolicy? policyV1, DateTimeOffset nowLocal)
{
    // Defaults: fall back to v1 policy windows if expanded surface is absent.
    ScheduleWindow? bedtime = policyV1?.BedtimeWindow;
    ScheduleWindow? school = policyV1?.SchoolWindow;
    ScheduleWindow? homework = policyV1?.HomeworkWindow;

    int? dailyMinutes = null;

    if (policySurface is not { } pol || pol.ValueKind != JsonValueKind.Object)
        return (dailyMinutes, bedtime, school, homework);

    if (pol.TryGetProperty("timeBudget", out var tb) && tb.ValueKind == JsonValueKind.Object)
    {
        if (tb.TryGetProperty("dailyMinutes", out var dm))
        {
            if (dm.ValueKind == JsonValueKind.Number && dm.TryGetInt32(out var v)) dailyMinutes = v;
            else if (dm.ValueKind == JsonValueKind.String && int.TryParse(dm.GetString(), out var v2)) dailyMinutes = v2;
        }


        // Per-day overrides (Mon..Sun)
        if (tb.TryGetProperty("perDayMinutes", out var pd) && pd.ValueKind == JsonValueKind.Object)
        {
            var key = nowLocal.DayOfWeek switch
            {
                DayOfWeek.Monday => "Mon",
                DayOfWeek.Tuesday => "Tue",
                DayOfWeek.Wednesday => "Wed",
                DayOfWeek.Thursday => "Thu",
                DayOfWeek.Friday => "Fri",
                DayOfWeek.Saturday => "Sat",
                DayOfWeek.Sunday => "Sun",
                _ => null
            };
            if (key is not null && pd.TryGetProperty(key, out var ov))
            {
                if (ov.ValueKind == JsonValueKind.Number && ov.TryGetInt32(out var ovn)) dailyMinutes = ovn;
                else if (ov.ValueKind == JsonValueKind.String && int.TryParse(ov.GetString(), out var ovs)) dailyMinutes = ovs;
            }
        }
        if (tb.TryGetProperty("schedules", out var sch) && sch.ValueKind == JsonValueKind.Object)
        {
            bedtime = ReadWindow(sch, "bedtime", bedtime);
            school = ReadWindow(sch, "school", school);
            homework = ReadWindow(sch, "homework", homework);
        }
    }

    return (dailyMinutes, bedtime, school, homework);

    static ScheduleWindow? ReadWindow(JsonElement schedules, string key, ScheduleWindow? fallback)
    {
        if (!schedules.TryGetProperty(key, out var w) || w.ValueKind != JsonValueKind.Object) return fallback;

        bool enabled = fallback?.Enabled == true;
        string start = fallback?.StartLocal ?? "22:00";
        string end = fallback?.EndLocal ?? "07:00";

        if (w.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
            enabled = en.GetBoolean();
        if (w.TryGetProperty("startLocal", out var st) && st.ValueKind == JsonValueKind.String)
            start = st.GetString() ?? start;
        if (w.TryGetProperty("endLocal", out var et) && et.ValueKind == JsonValueKind.String)
            end = et.GetString() ?? end;

        return new ScheduleWindow(enabled, start, end);
    }
}

private static bool IsWindowActive(DateTimeOffset nowLocal, ScheduleWindow? w)
{
    if (w is null || !w.Enabled) return false;
    if (!TimeSpan.TryParse((w.StartLocal ?? string.Empty).Trim(), out var start)) return false;
    if (!TimeSpan.TryParse((w.EndLocal ?? string.Empty).Trim(), out var end)) return false;

    var now = nowLocal.TimeOfDay;

    // Cross-midnight window (e.g., 22:00 -> 07:00)
    if (end <= start)
    {
        return now >= start || now < end;
    }

    // Same-day window
    return now >= start && now < end;
}

private static ChildPolicy? TryMapPolicyFromLocalProfileJson(JsonElement profile, ChildId childId)
    {
        try
        {
            if (profile.ValueKind != JsonValueKind.Object) return null;

            // Defaults should align with Parent UI defaultSettingsProfile + PATCH 10A `policy` additive defaults.
            var mode = SafetyMode.Open;
            int? screenMins = null;
            var bedtimeEnabled = false;
            var bedtimeStart = "22:00";
            var bedtimeEnd = "07:00";

            var schoolEnabled = false;
            var schoolStart = "09:00";
            var schoolEnd = "15:00";

            var homeworkEnabled = false;
            var homeworkStart = "17:00";
            var homeworkEnd = "19:00";

            // policy.mode (string)
            if (profile.TryGetProperty("policy", out var pol) && pol.ValueKind == JsonValueKind.Object)
            {
                if (pol.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    var s = (m.GetString() ?? string.Empty).Trim();
                    if (Enum.TryParse<SafetyMode>(s, ignoreCase: true, out var parsed))
                        mode = parsed;
                }
            }



// policy.timeBudget (expanded surface)
if (profile.TryGetProperty("policy", out var pol2) && pol2.ValueKind == JsonValueKind.Object)
{
    if (pol2.TryGetProperty("timeBudget", out var tb) && tb.ValueKind == JsonValueKind.Object)
    {
        if (tb.TryGetProperty("dailyMinutes", out var dm))
        {
            if (dm.ValueKind == JsonValueKind.Number && dm.TryGetInt32(out var v))
                screenMins = v;
            else if (dm.ValueKind == JsonValueKind.String && int.TryParse(dm.GetString(), out var v2))
                screenMins = v2;
        }

        if (tb.TryGetProperty("schedules", out var sch) && sch.ValueKind == JsonValueKind.Object)
        {
            if (sch.TryGetProperty("bedtime", out var bw) && bw.ValueKind == JsonValueKind.Object)
            {
                if (bw.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                    bedtimeEnabled = en.GetBoolean();
                if (bw.TryGetProperty("startLocal", out var st) && st.ValueKind == JsonValueKind.String)
                    bedtimeStart = st.GetString() ?? bedtimeStart;
                if (bw.TryGetProperty("endLocal", out var et) && et.ValueKind == JsonValueKind.String)
                    bedtimeEnd = et.GetString() ?? bedtimeEnd;
            }

            if (sch.TryGetProperty("school", out var sw) && sw.ValueKind == JsonValueKind.Object)
            {
                if (sw.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                    schoolEnabled = en.GetBoolean();
                if (sw.TryGetProperty("startLocal", out var st) && st.ValueKind == JsonValueKind.String)
                    schoolStart = st.GetString() ?? schoolStart;
                if (sw.TryGetProperty("endLocal", out var et) && et.ValueKind == JsonValueKind.String)
                    schoolEnd = et.GetString() ?? schoolEnd;
            }

            if (sch.TryGetProperty("homework", out var hw) && hw.ValueKind == JsonValueKind.Object)
            {
                if (hw.TryGetProperty("enabled", out var en) && (en.ValueKind == JsonValueKind.True || en.ValueKind == JsonValueKind.False))
                    homeworkEnabled = en.GetBoolean();
                if (hw.TryGetProperty("startLocal", out var st) && st.ValueKind == JsonValueKind.String)
                    homeworkStart = st.GetString() ?? homeworkStart;
                if (hw.TryGetProperty("endLocal", out var et) && et.ValueKind == JsonValueKind.String)
                    homeworkEnd = et.GetString() ?? homeworkEnd;
            }
        }
    }
}
            // permissions
            if (profile.TryGetProperty("permissions", out var perms) && perms.ValueKind == JsonValueKind.Object)
            {
                if (perms.TryGetProperty("bedtime", out var bt) && bt.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    bedtimeEnabled = bt.GetBoolean();
                if (perms.TryGetProperty("school", out var sc) && sc.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    schoolEnabled = sc.GetBoolean();
                if (perms.TryGetProperty("homework", out var hw) && hw.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    homeworkEnabled = hw.GetBoolean();
            }

            // limits
            if (profile.TryGetProperty("limits", out var lim) && lim.ValueKind == JsonValueKind.Object)
            {
                if (lim.TryGetProperty("screenMinutesPerDay", out var sm))
                {
                    if (sm.ValueKind == JsonValueKind.Number && sm.TryGetInt32(out var v))
                        screenMins = v;
                    else if (sm.ValueKind == JsonValueKind.String && int.TryParse(sm.GetString(), out var v2))
                        screenMins = v2;
                }
                if (lim.TryGetProperty("bedtimeStart", out var bs) && bs.ValueKind == JsonValueKind.String)
                    bedtimeStart = bs.GetString() ?? bedtimeStart;
                if (lim.TryGetProperty("bedtimeEnd", out var be) && be.ValueKind == JsonValueKind.String)
                    bedtimeEnd = be.GetString() ?? bedtimeEnd;

                if (lim.TryGetProperty("schoolStart", out var ss) && ss.ValueKind == JsonValueKind.String)
                    schoolStart = ss.GetString() ?? schoolStart;
                if (lim.TryGetProperty("schoolEnd", out var se) && se.ValueKind == JsonValueKind.String)
                    schoolEnd = se.GetString() ?? schoolEnd;

                if (lim.TryGetProperty("homeworkStart", out var hs) && hs.ValueKind == JsonValueKind.String)
                    homeworkStart = hs.GetString() ?? homeworkStart;
                if (lim.TryGetProperty("homeworkEnd", out var he) && he.ValueKind == JsonValueKind.String)
                    homeworkEnd = he.GetString() ?? homeworkEnd;
            }

            var bedtimeWindow = new ScheduleWindow(
                Enabled: bedtimeEnabled,
                StartLocal: bedtimeStart,
                EndLocal: bedtimeEnd);

            var schoolWindow = new ScheduleWindow(
                Enabled: schoolEnabled,
                StartLocal: schoolStart,
                EndLocal: schoolEnd);

            var homeworkWindow = new ScheduleWindow(
                Enabled: homeworkEnabled,
                StartLocal: homeworkStart,
                EndLocal: homeworkEnd);

            // Build a v1 ChildPolicy snapshot from profile settings.
            // Version is synthetic in Local Mode (SSOT profile is the source of truth).
            return new ChildPolicy(
                ChildId: childId,
                Version: new PolicyVersion(1),
                Mode: mode,
                UpdatedAtUtc: DateTimeOffset.UtcNow,
                UpdatedBy: "local_profile",
                DailyScreenTimeLimitMinutes: screenMins,
                BedtimeWindow: bedtimeWindow,
                SchoolWindow: schoolWindow,
                HomeworkWindow: homeworkWindow);
        }
        catch
        {
            return null;
        }
    }

    private static string ComputePolicyFingerprint(ChildPolicy policy)
    {
        // Keep it stable and privacy-first. This is used only to avoid spamming Activity with duplicates.
        return string.Join("|", new string[]
        {
            policy.Mode.ToString(),
            (policy.DailyScreenTimeLimitMinutes ?? 0).ToString(),
            policy.BedtimeWindow?.Enabled == true ? "1" : "0",
            policy.BedtimeWindow?.StartLocal ?? string.Empty,
            policy.BedtimeWindow?.EndLocal ?? string.Empty,
            policy.SchoolWindow?.Enabled == true ? "1" : "0",
            policy.SchoolWindow?.StartLocal ?? string.Empty,
            policy.SchoolWindow?.EndLocal ?? string.Empty,
            policy.HomeworkWindow?.Enabled == true ? "1" : "0",
            policy.HomeworkWindow?.StartLocal ?? string.Empty,
            policy.HomeworkWindow?.EndLocal ?? string.Empty,
        });
    }

	    private static string BuildPolicyAppliedDetails(ChildPolicy policyV1, int? policyVersion, DateTimeOffset? effectiveAtUtc, JsonElement? policySurface, string source)
    {
        try
        {
            var resolved = ResolveModeWithPrecedence(policySurface, policyV1.Mode);

            // Keep details machine-readable (Parent can render this later).
	            var payload = new
            {
	                source,
                policyVersion,
                effectiveAtUtc,
                mode = resolved.ToString(),
                v1 = new
                {
                    mode = policyV1.Mode.ToString(),
                    dailyScreenTimeLimitMinutes = policyV1.DailyScreenTimeLimitMinutes,
                    bedtimeEnabled = policyV1.BedtimeWindow?.Enabled == true,
                    bedtimeStartLocal = policyV1.BedtimeWindow?.StartLocal,
                    bedtimeEndLocal = policyV1.BedtimeWindow?.EndLocal,
                    schoolEnabled = policyV1.SchoolWindow?.Enabled == true,
                    schoolStartLocal = policyV1.SchoolWindow?.StartLocal,
                    schoolEndLocal = policyV1.SchoolWindow?.EndLocal,
                    homeworkEnabled = policyV1.HomeworkWindow?.Enabled == true,
                    homeworkStartLocal = policyV1.HomeworkWindow?.StartLocal,
                    homeworkEndLocal = policyV1.HomeworkWindow?.EndLocal
                },
                hasExpandedSurface = policySurface is not null
            };

            return JsonSerializer.Serialize(payload, JsonDefaults.Options);
        }
        catch
        {
            // Backstop: never throw from activity logging.
            return $"policyVersion={policyVersion};mode={policyV1.Mode}";
        }
    }

    private static SafetyMode ResolveModeWithPrecedence(JsonElement? policySurface, SafetyMode? fallback)
    {
        // Phase A precedence scaffold: Lockdown overrides all, then Bedtime, then Homework, then Home, then Open.
        // This is a pure decision function; OS-level enforcement is out of scope for Phase A.
        var raw = (string?)null;
        if (policySurface is { } pol && pol.ValueKind == JsonValueKind.Object)
        {
            if (pol.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String)
                raw = (m.GetString() ?? string.Empty).Trim();
        }

        if (string.IsNullOrWhiteSpace(raw))
            return fallback ?? SafetyMode.Open;

        // Accept both enum names and common casing.
        if (!Enum.TryParse<SafetyMode>(raw, ignoreCase: true, out var desired))
            desired = fallback ?? SafetyMode.Open;

        // In Phase A, precedence only matters when a higher-priority mode is requested.
        // (We don't yet compute time-based mode flips here.)
        return desired;
    }

    private static void EmitWouldEnforceEvents(ActivityOutbox outbox, string? deviceId, SafetyMode resolvedMode, JsonElement? policySurface, ChildPolicy? policyV1)
    {
        // Phase A: observable enforcement scaffolding (no hard blocking required).
        // We emit events based on config presence so Parent can observe end-to-end wiring.
        if (policySurface is not { } pol || pol.ValueKind != JsonValueKind.Object)
        {
            // If we don't have the expanded surface yet, still emit minimal v1 signals.
            if (policyV1?.DailyScreenTimeLimitMinutes is > 0)
            {
                outbox.Enqueue(new ActivityOutbox.LocalActivityEvent(Guid.NewGuid(), DateTimeOffset.UtcNow,
                    "policy_would_enforce_time_budget", null,
                    JsonSerializer.Serialize(new { mode = resolvedMode.ToString(), dailyMinutes = policyV1.DailyScreenTimeLimitMinutes }, JsonDefaults.Options),
                    deviceId));
            }
            return;
        }

        void Enq(string kind, object payload)
        {
            outbox.Enqueue(new ActivityOutbox.LocalActivityEvent(
                EventId: Guid.NewGuid(),
                OccurredAtUtc: DateTimeOffset.UtcNow,
                Kind: kind,
                App: null,
                Details: JsonSerializer.Serialize(payload, JsonDefaults.Options),
                DeviceId: deviceId));
        }

        // Time budget
        int? dailyMinutes = null;
        bool hasSchedules = false;
        if (pol.TryGetProperty("timeBudget", out var tb) && tb.ValueKind == JsonValueKind.Object)
        {
            if (tb.TryGetProperty("dailyMinutes", out var dm) && dm.ValueKind == JsonValueKind.Number && dm.TryGetInt32(out var dmi))
                dailyMinutes = dmi;
            if (tb.TryGetProperty("schedules", out var sch) && sch.ValueKind == JsonValueKind.Object)
            {
                foreach (var _ in sch.EnumerateObject()) { hasSchedules = true; break; }
            }
        }
        if ((dailyMinutes is > 0) || hasSchedules)
        {
            Enq("policy_would_enforce_time_budget", new { mode = resolvedMode.ToString(), dailyMinutes, hasSchedules });
        }

        // Apps
        bool appsWould = false;
        if (pol.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Object)
        {
            bool allowListEnabled = apps.TryGetProperty("allowListEnabled", out var ale) && ale.ValueKind is JsonValueKind.True or JsonValueKind.False && ale.GetBoolean();
            bool blockNewApps = apps.TryGetProperty("blockNewApps", out var bna) && bna.ValueKind is JsonValueKind.True or JsonValueKind.False && bna.GetBoolean();
            int denyCount = apps.TryGetProperty("denyList", out var dl) && dl.ValueKind == JsonValueKind.Array ? dl.GetArrayLength() : 0;
            int allowCount = apps.TryGetProperty("allowList", out var al) && al.ValueKind == JsonValueKind.Array ? al.GetArrayLength() : 0;
            int perAppCount = apps.TryGetProperty("perAppLimits", out var pal) && pal.ValueKind == JsonValueKind.Array ? pal.GetArrayLength() : 0;
            appsWould = allowListEnabled || blockNewApps || denyCount > 0 || allowCount > 0 || perAppCount > 0;
            if (appsWould)
                Enq("policy_would_enforce_apps", new { mode = resolvedMode.ToString(), allowListEnabled, blockNewApps, denyCount, allowCount, perAppCount });
        }

        // Web
        if (pol.TryGetProperty("web", out var web) && web.ValueKind == JsonValueKind.Object)
        {
            int domainRulesCount = web.TryGetProperty("domainRules", out var dr) && dr.ValueKind == JsonValueKind.Array ? dr.GetArrayLength() : 0;
            bool safeSearch = web.TryGetProperty("safeSearch", out var ss) && ss.ValueKind is JsonValueKind.True or JsonValueKind.False && ss.GetBoolean();
            bool blockAdult = web.TryGetProperty("blockAdult", out var ba) && ba.ValueKind is JsonValueKind.True or JsonValueKind.False && ba.GetBoolean();
            int catCount = web.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Object ? cats.EnumerateObject().Count() : 0;
            if (domainRulesCount > 0 || safeSearch || blockAdult || catCount > 0)
                Enq("policy_would_enforce_web", new { mode = resolvedMode.ToString(), domainRulesCount, safeSearch, blockAdult, categoriesConfigured = catCount });
        }

        // Location
        if (pol.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object)
        {
            bool sharingEnabled = loc.TryGetProperty("sharingEnabled", out var se) && se.ValueKind is JsonValueKind.True or JsonValueKind.False && se.GetBoolean();
            int geofencesCount = loc.TryGetProperty("geofences", out var gf) && gf.ValueKind == JsonValueKind.Array ? gf.GetArrayLength() : 0;
            if (sharingEnabled || geofencesCount > 0)
                Enq("policy_would_enforce_location", new { mode = resolvedMode.ToString(), sharingEnabled, geofencesCount });
        }
    }


private void TryEnforce(SafetyMode enforcedMode, ChildPolicy? policy, bool budgetDepleted, bool bedtimeActive, string? foregroundExe, AppUsageTracker appTracker, Grant[]? activeGrants, ref DateTimeOffset lastLockAttemptUtc, ref DateTimeOffset lastUxNavigateUtc)
    {
        try
        {
            var unblockApps = BuildUnblockAppSet(activeGrants);

            // K5: deny list (existing field name)
            var denied = (policy?.BlockedProcessNames is { Length: > 0 })
                ? policy.BlockedProcessNames.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();

            // K5: optional allow list
            var allowListEnabled = policy?.AppsAllowListEnabled == true;
            var allowed = (policy?.AllowedProcessNames is { Length: > 0 })
                ? policy.AllowedProcessNames.Where(s => !string.IsNullOrWhiteSpace(s)).ToList()
                : new List<string>();

            var perAppLimits = policy?.PerAppDailyLimits ?? Array.Empty<PerAppLimit>();

            // Deny list is enforced whenever apps are detected.
            if (denied.Count > 0)
            {
                var deniedToEnforce = denied
                    .Select(NormalizeExe)
                    .Where(exe => exe.Length > 0 && !unblockApps.Contains(exe))
                    .ToList();

                if (deniedToEnforce.Count > 0)
                {
                    TryKillProcesses(deniedToEnforce, reason: "deny_list", appTracker: appTracker, kindForUx: "app", ref lastUxNavigateUtc);
                }
            }

            // Per-app limits are enforced on the foreground app (best-effort).
            if (!string.IsNullOrWhiteSpace(foregroundExe) && perAppLimits.Length > 0)
            {
                var fg = NormalizeExe(foregroundExe);
                if (unblockApps.Contains(fg))
                {
                    // Grant overrides per-app limits.
                    goto AfterPerApp;
                }

                var limit = perAppLimits.FirstOrDefault(l => NormalizeExe(l.ProcessName) == fg);
                if (limit is not null && limit.LimitMinutes > 0)
                {
                    var usedSec = appTracker.GetUsedSecondsTodayFor(fg);
                    if (usedSec >= (limit.LimitMinutes * 60))
                    {
                        TryKillProcesses(new[] { fg }, reason: "per_app_limit", appTracker: appTracker, kindForUx: "app", ref lastUxNavigateUtc);
                    }
                }
            }

        AfterPerApp:

            // Allow list is foreground-only to reduce OS risk.
            if (allowListEnabled && allowed.Count > 0 && !string.IsNullOrWhiteSpace(foregroundExe))
            {
                var fg = NormalizeExe(foregroundExe);
                if (unblockApps.Contains(fg))
                {
                    // Grant overrides allow-list blocks.
                    goto AfterAllowList;
                }

                if (!IsEssentialProcess(fg) && !allowed.Any(a => NormalizeExe(a) == fg))
                {
                    TryKillProcesses(new[] { fg }, reason: "allow_list", appTracker: appTracker, kindForUx: "app", ref lastUxNavigateUtc);
                }
            }

        AfterAllowList:

            // Lockdown still means "kill defaults" + lock if budget depleted.
            if (enforcedMode == SafetyMode.Lockdown || enforcedMode == SafetyMode.Bedtime)
            {
                if (denied.Count == 0)
                {
                    TryKillProcesses(new[] { "notepad.exe" }, reason: "lockdown_default", appTracker: appTracker, kindForUx: "app", ref lastUxNavigateUtc);
                }

                if (budgetDepleted || bedtimeActive)
                {
                    var now = DateTimeOffset.UtcNow;
                    if (now - lastLockAttemptUtc > TimeSpan.FromSeconds(30))
                    {
                        lastLockAttemptUtc = now;
                        // K7: show an explainable local screen.
                        var kind = budgetDepleted ? "time" : "time";
                        var reason = budgetDepleted ? "daily_budget" : "bedtime";
                        ChildUxNavigation.TryOpenBlocked(kind, target: "screen_time", reason, ref lastUxNavigateUtc, _logger);
                        if (Win32.LockWorkStation())
                        {
                            _logger.LogInformation("Time restriction active: locked workstation");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _lastEnforcementErrorAtUtc = DateTimeOffset.UtcNow;
            _lastEnforcementError = ex.GetType().Name + ": " + ex.Message;
            _logger.LogWarning(ex, "Enforcement attempt failed");
        }
    }

    

private static int ComputeExtraScreenTimeMinutes(Grant[]? activeGrants)
{
    if (activeGrants is null || activeGrants.Length == 0) return 0;
    var sum = 0;
    foreach (var g in activeGrants)
    {
        if (g.Type != GrantType.ExtraScreenTime) continue;
        if (g.ExtraMinutes is null) continue;
        if (g.ExtraMinutes.Value <= 0) continue;
        sum += g.ExtraMinutes.Value;
    }
    return sum;
}

private static HashSet<string> BuildUnblockAppSet(Grant[]? activeGrants)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (activeGrants is null) return set;

        foreach (var g in activeGrants)
        {
            if (g.Type != GrantType.UnblockApp) continue;
            var exe = NormalizeExe(g.Target);
            if (exe.Length > 0) set.Add(exe);
        }
        return set;
    }

    private static ChildPolicy? ApplyUnblockSiteGrants(ChildPolicy? policy, Grant[]? activeGrants)
    {
        if (policy is null || activeGrants is null || activeGrants.Length == 0) return policy;

        var sites = activeGrants
            .Where(g => g.Type == GrantType.UnblockSite)
            .Select(g => (g.Target ?? string.Empty).Trim().ToLowerInvariant())
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sites.Length == 0) return policy;

        var allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in policy.WebAllowedDomains ?? Array.Empty<string>())
        {
            var n = (d ?? string.Empty).Trim().ToLowerInvariant();
            if (n.Length > 0) allow.Add(n);
        }
        foreach (var s in sites) allow.Add(s);

        // If a domain is explicitly blocked, the allow list should still override.
        // WebFilterManager already removes allow exceptions from the final blocked set.
        return policy with { WebAllowedDomains = allow.ToArray() };
    }

    private static bool IsEssentialProcess(string exe)
        => exe is "explorer.exe" or "dwm.exe" or "csrss.exe" or "winlogon.exe" or "services.exe" or "lsass.exe";

    private static string NormalizeExe(string name)
    {
        var n = (name ?? string.Empty).Trim().ToLowerInvariant();
        if (n.Length == 0) return n;
        return n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n : (n + ".exe");
    }

    private void TryKillProcesses(IEnumerable<string> processNames, string reason, AppUsageTracker appTracker, string kindForUx, ref DateTimeOffset lastUxNavigateUtc)
    {
        foreach (var raw in processNames)
        {
            var exe = NormalizeExe(raw);
            if (exe.Length == 0) continue;

            var nameNoExt = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe[..^4] : exe;

            foreach (var p in System.Diagnostics.Process.GetProcessesByName(nameNoExt))
            {
                try
                {
                    p.Kill(entireProcessTree: true);
                    appTracker.RecordBlocked(exe, reason);
                    _logger.LogInformation("Terminated process {ProcessName} (pid {Pid}) reason={Reason}", p.ProcessName, p.Id, reason);

                    // K7: show the child an explainable screen (best-effort, throttled).
                    ChildUxNavigation.TryOpenBlocked(
                        kind: kindForUx,
                        target: exe,
                        reason: reason,
                        ref lastUxNavigateUtc,
                        _logger);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not terminate process {ProcessName} (pid {Pid})", p.ProcessName, p.Id);
                }
            }
        }
    }

}
