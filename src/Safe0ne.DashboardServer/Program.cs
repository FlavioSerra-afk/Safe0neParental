using Microsoft.Extensions.Primitives;
using System.Text.Json;
using System.Text.Json.Nodes;
using Safe0ne.DashboardServer.ControlPlane;
using Safe0ne.DashboardServer.Reports;
using Safe0ne.DashboardServer.PolicyEngine;
using Safe0ne.DashboardServer.LocalApi;
using Safe0ne.Shared.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalOnly", p =>
        p.WithOrigins("http://127.0.0.1:8765", "http://localhost:8765")
         .AllowAnyHeader()
         .AllowAnyMethod());
});

// Control Plane store (file-backed persistence).
builder.Services.AddSingleton<JsonFileControlPlane>();

// 16W9: Local SSOT-backed reports scheduler (best-effort; never crashes server).
builder.Services.AddHostedService<ReportSchedulerService>();

var app = builder.Build();

app.UseCors("LocalOnly");

// Minimal health endpoint for the Parent App to display status chips.
app.MapGet("/api/health", () => Results.Json(new
{
    ok = true,
    service = "Safe0ne.DashboardServer",
    tsUtc = DateTime.UtcNow
}, JsonDefaults.Options));

static bool TryGetDeviceToken(HttpRequest req, out string token)
{
    token = "";
    if (!req.Headers.TryGetValue(AgentAuth.DeviceTokenHeaderName, out StringValues values))
    {
        return false;
    }
    var raw = values.ToString();
    if (string.IsNullOrWhiteSpace(raw)) return false;
    token = raw.Trim();
    return true;
}

// --- Control Plane v1 ---
app.MapGet($"/api/{ApiVersions.V1}/children", (JsonFileControlPlane cp) =>
{
    var data = cp.GetChildren().ToList();
    return Results.Json(new ApiResponse<List<ChildProfile>>(data, null), JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/policy", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    if (!cp.TryGetPolicy(id, out var policy))
    {
        return Results.Json(
            new ApiResponse<ChildPolicy>(null, new ApiError("not_found", "Child policy not found")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status404NotFound);
    }
    return Results.Json(new ApiResponse<ChildPolicy>(policy, null), JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/effective", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    if (!cp.TryGetPolicy(id, out var policy))
    {
        return Results.Json(
            new ApiResponse<EffectiveChildState>(null, new ApiError("not_found", "Child policy not found")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status404NotFound);
    }

    var now = DateTimeOffset.UtcNow;
    var effective = PolicyEvaluator.Evaluate(policy, now);

    // K8/P11: surface active grants in effective state (additive contract field).
    var grants = cp.GetActiveGrants(id, now).ToArray();
    effective = effective with { ActiveGrants = grants };

    return Results.Json(new ApiResponse<EffectiveChildState>(effective, null), JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/status", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    if (!cp.TryGetStatus(id, out var status))
    {
        return Results.Json(
            new ApiResponse<ChildAgentStatus>(null, new ApiError("not_found", "Child status not found")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status404NotFound);
    }
    return Results.Json(new ApiResponse<ChildAgentStatus>(status, null), JsonDefaults.Options);
});

// LEGACY-COMPAT: Some older/cached UI builds called a malformed status route of the form:
//   /api/v1/childre_<id>/status
// where <id> was not a GUID and the segment was missing "children/".
//
// This shim prevents repeated 404 spam in embedded WebView caches and is safe because it
// returns an explicit ApiError while still using HTTP 200 for compatibility.
// RemoveAfter: UI-Cache-Bust-Rollout | Tracking: Docs/00_Shared/Legacy-Code-Registry.md
app.MapGet($"/api/{ApiVersions.V1}/childre_{{id}}/status", (string id) =>
{
    return Results.Json(
        new ApiResponse<ChildAgentStatus>(null, new ApiError("legacy_route", "Legacy status route is deprecated", id)),
        JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/devices", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var devices = cp.GetDevices(id).ToList();
    return Results.Json(new ApiResponse<List<ChildDeviceSummary>>(devices, null), JsonDefaults.Options);
});

// K8: Child submits access request (more time / unblock app / unblock site).
// If paired devices exist, require a valid device token header.
app.MapPost($"/api/{ApiVersions.V1}/children/{{childId:guid}}/requests", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    if (cp.HasPairedDevices(id))
    {
        if (!TryGetDeviceToken(req, out var token) || !cp.TryValidateDeviceToken(id, token, out _))
        {
            return Results.Unauthorized();
        }
    }

    var body = await req.ReadFromJsonAsync<CreateAccessRequestRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<AccessRequest>(null, new ApiError("bad_request", "Missing or invalid JSON body")));
    }

    var created = cp.CreateOrGetRequest(id, body);
    return Results.Json(new ApiResponse<AccessRequest>(created, null), JsonDefaults.Options);
});

// Child polls their requests (useful for status in child UX).
app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/requests", (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    if (cp.HasPairedDevices(id))
    {
        if (!TryGetDeviceToken(req, out var token) || !cp.TryValidateDeviceToken(id, token, out _))
        {
            return Results.Unauthorized();
        }
    }

    var list = cp.GetRequests(id, null, 200).ToList();
    return Results.Json(new ApiResponse<List<AccessRequest>>(list, null), JsonDefaults.Options);
});

// P11: Parent requests inbox (optionally filter by childId and status).
app.MapGet($"/api/{ApiVersions.V1}/requests", (Guid? childId, string? status, int? take, JsonFileControlPlane cp) =>
{
    ChildId? id = null;
    if (childId is not null && childId.Value != Guid.Empty)
    {
        id = new ChildId(childId.Value);
    }

    AccessRequestStatus? st = null;
    if (!string.IsNullOrWhiteSpace(status) && Enum.TryParse<AccessRequestStatus>(status, ignoreCase: true, out var parsed))
    {
        st = parsed;
    }

    var list = cp.GetRequests(id, st, take ?? 200).ToList();
    return Results.Json(new ApiResponse<List<AccessRequest>>(list, null), JsonDefaults.Options);
});

// P11: Parent approve/deny.
app.MapPost($"/api/{ApiVersions.V1}/requests/{{requestId:guid}}/decide", async (Guid requestId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var body = await req.ReadFromJsonAsync<DecideAccessRequestRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<AccessRequest>(null, new ApiError("bad_request", "Missing or invalid JSON body")));
    }

    if (!cp.TryDecideRequest(requestId, body, out var updated, out var grant))
    {
        return Results.Json(new ApiResponse<AccessRequest>(null, new ApiError("not_found", "Request not found")), JsonDefaults.Options, statusCode: StatusCodes.Status404NotFound);
    }

    return Results.Json(new ApiResponse<object>(new { request = updated, grant }, null), JsonDefaults.Options);
});

// K1 Pairing
app.MapPost($"/api/{ApiVersions.V1}/children/{{childId:guid}}/pair/start", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var resp = cp.StartPairing(id);
    return Results.Json(new ApiResponse<PairingStartResponse>(resp, null), JsonDefaults.Options);
});

app.MapPost($"/api/{ApiVersions.V1}/children/{{childId:guid}}/pair/complete", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var body = await req.ReadFromJsonAsync<PairingCompleteRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<PairingCompleteResponse>(null, new ApiError("bad_request", "Missing or invalid JSON body")));
    }

    try
    {
        var resp = cp.CompletePairing(id, body);
        return Results.Json(new ApiResponse<PairingCompleteResponse>(resp, null), JsonDefaults.Options);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Json(new ApiResponse<PairingCompleteResponse>(null, new ApiError("pairing_failed", ex.Message)), JsonDefaults.Options, statusCode: StatusCodes.Status400BadRequest);
    }
});

// Heartbeat: if the child has paired devices, require a valid device token header.
app.MapPost($"/api/{ApiVersions.V1}/children/{{childId:guid}}/heartbeat", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    var body = await req.ReadFromJsonAsync<ChildAgentHeartbeatRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<ChildAgentStatus>(null, new ApiError("bad_request", "Missing or invalid JSON body")));
    }

    Guid? deviceId = null;
    var authenticated = false;

    if (cp.HasPairedDevices(id))
    {
        if (!TryGetDeviceToken(req, out var token) || !cp.TryValidateDeviceToken(id, token, out var did))
        {
            return Results.Unauthorized();
        }
        authenticated = true;
        deviceId = did;
    }

    if (!cp.TryGetPolicy(id, out var policy))
    {
        return Results.Json(
            new ApiResponse<ChildAgentStatus>(null, new ApiError("not_found", "Child policy not found")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status404NotFound);
    }

    // The control plane computes the effective state at receipt time.
    var now = DateTimeOffset.UtcNow;
    var effective = PolicyEvaluator.Evaluate(policy, now);

    // K8/P11: attach active grants.
    var activeGrants = cp.GetActiveGrants(id, now).ToArray();
    effective = effective with { ActiveGrants = activeGrants };

    // K4: if the agent reports budget depletion, force Lockdown regardless of configured mode.
    var limit = policy.DailyScreenTimeLimitMinutes;
    // Extra minutes are additive on top of the daily limit.
    if (limit is not null && limit.Value > 0)
    {
        var extra = activeGrants.Where(g => g.Type == GrantType.ExtraScreenTime).Select(g => g.ExtraMinutes ?? 0).Sum();
        if (extra > 0) limit = limit.Value + extra;
    }
    if (body.ScreenTime is not null && limit is not null && limit.Value > 0)
    {
        var usedMins = Math.Max(0, body.ScreenTime.UsedSecondsToday / 60);
        var depleted = body.ScreenTime.BudgetDepleted || usedMins >= limit.Value;
        if (depleted)
        {
            effective = effective with
            {
                EffectiveMode = SafetyMode.Lockdown,
                ReasonCode = "screen_time_budget"
            };
        }
    }
    var status = cp.UpsertStatus(id, body, effective, authenticated, deviceId);
    return Results.Json(new ApiResponse<ChildAgentStatus>(status, null), JsonDefaults.Options);
});



// K2 Commands (Parent -> Control Plane -> Agent)
app.MapPost($"/api/{ApiVersions.V1}/children/{{childId:guid}}/commands", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var body = await req.ReadFromJsonAsync<CreateChildCommandRequest>(JsonDefaults.Options);
    if (body is null || string.IsNullOrWhiteSpace(body.Type))
    {
        return Results.BadRequest(new ApiResponse<ChildCommand>(null, new ApiError("bad_request", "Missing or invalid JSON body")));
    }

    var cmd = cp.CreateCommand(id, body);
    return Results.Json(new ApiResponse<ChildCommand>(cmd, null), JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/commands", (Guid childId, int? take, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var list = cp.GetCommands(id, take ?? 50).ToList();
    return Results.Json(new ApiResponse<List<ChildCommand>>(list, null), JsonDefaults.Options);
});

// Agent poll: if paired devices exist, require token.
app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/commands/pending", (Guid childId, int? max, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    if (cp.HasPairedDevices(id))
    {
        if (!TryGetDeviceToken(req, out var token) || !cp.TryValidateDeviceToken(id, token, out _))
        {
            return Results.Unauthorized();
        }
    }

    var list = cp.GetPendingCommands(id, max ?? 20).ToList();
    return Results.Json(new ApiResponse<List<ChildCommand>>(list, null), JsonDefaults.Options);
});

// Agent ack: require token when paired.
app.MapPost($"/api/{ApiVersions.V1}/children/{{childId:guid}}/commands/{{commandId:guid}}/ack", async (Guid childId, Guid commandId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var body = await req.ReadFromJsonAsync<AckChildCommandRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<ChildCommand>(null, new ApiError("bad_request", "Missing or invalid JSON body")));
    }

    Guid deviceId = Guid.Empty;
    if (cp.HasPairedDevices(id))
    {
        if (!TryGetDeviceToken(req, out var token) || !cp.TryValidateDeviceToken(id, token, out var did))
        {
            return Results.Unauthorized();
        }
        deviceId = did;
    }

    if (!cp.TryAckCommand(id, commandId, deviceId, body, out var updated))
    {
        return Results.Json(new ApiResponse<ChildCommand>(null, new ApiError("not_found", "Command not found")), JsonDefaults.Options, statusCode: StatusCodes.Status404NotFound);
    }

    return Results.Json(new ApiResponse<ChildCommand>(updated, null), JsonDefaults.Options);
});



// K9 Diagnostics bundle upload/download
app.MapPut($"/api/{ApiVersions.V1}/children/{{childId:guid}}/diagnostics/bundles", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    // If paired devices exist, require token.
    if (cp.HasPairedDevices(id))
    {
        if (!TryGetDeviceToken(req, out var token) || !cp.TryValidateDeviceToken(id, token, out _))
        {
            return Results.Unauthorized();
        }
    }

    var fileName = req.Headers["X-Safe0ne-Diagnostics-FileName"].FirstOrDefault() ?? "diagnostics.zip";

    // Body is raw zip bytes.
    using var ms = new MemoryStream();
    await req.Body.CopyToAsync(ms);
    ms.Position = 0;

    var info = cp.SaveDiagnosticsBundle(id, fileName, ms);
    return Results.Json(new ApiResponse<DiagnosticsBundleInfo>(info, null), JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/diagnostics/bundles/latest/info", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    if (!cp.TryGetLatestDiagnosticsBundle(id, out var info))
    {
        return Results.Json(new ApiResponse<DiagnosticsBundleInfo>(null, new ApiError("not_found", "No diagnostics bundle")), JsonDefaults.Options, statusCode: StatusCodes.Status404NotFound);
    }
    return Results.Json(new ApiResponse<DiagnosticsBundleInfo>(info, null), JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/diagnostics/bundles/latest", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    if (!cp.TryGetLatestDiagnosticsBundle(id, out var info, out var path))
    {
        return Results.Json(new ApiResponse<string>(null, new ApiError("not_found", "No diagnostics bundle")), JsonDefaults.Options, statusCode: StatusCodes.Status404NotFound);
    }

    return Results.File(path, "application/zip", fileDownloadName: info.FileName);
});
app.MapPut($"/api/{ApiVersions.V1}/children/{{childId:guid}}/policy", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    // Read raw JSON once so we can both:
    // 1) deserialize into the stable v1 request contract (unknown fields are ignored)
    // 2) extract additive "stub" fields that may not yet exist in the v1 contract
    string raw;
    using (var reader = new StreamReader(req.Body))
        raw = await reader.ReadToEndAsync();

    UpdateChildPolicyRequest? body = null;
    try
    {
        body = JsonSerializer.Deserialize<UpdateChildPolicyRequest>(raw, JsonDefaults.Options);
    }
    catch
    {
        body = null;
    }

    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<ChildPolicy>(null, new ApiError("bad_request", "Missing or invalid JSON body")));
    }

    bool? safeSearchEnabled = null;
    bool? youtubeRestrictedModeEnabled = null;

    try
    {
        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object)
        {
            if (root.TryGetProperty("webSafeSearchEnabled", out var ss) && ss.ValueKind is JsonValueKind.True or JsonValueKind.False)
                safeSearchEnabled = ss.GetBoolean();

            if (root.TryGetProperty("webYouTubeRestrictedModeEnabled", out var yt) && yt.ValueKind is JsonValueKind.True or JsonValueKind.False)
                youtubeRestrictedModeEnabled = yt.GetBoolean();
        }
    }
    catch
    {
        // ignore malformed extras
    }

    var updated = cp.UpsertPolicy(id, body);

    // PATCH 16T bridge: persist SafeSearch / YouTube Restricted Mode into the SSOT policy surface
    // under the Local Settings Profile (used by /api/local/children/{id}/policy and the Kid agent).
    try
    {
        if (safeSearchEnabled is not null || youtubeRestrictedModeEnabled is not null)
        {
            var profileJson = cp.GetOrCreateLocalSettingsProfileJson(id);
            var node = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject();

            var pol = node["policy"] as JsonObject ?? new JsonObject();
            node["policy"] = pol;

            var web = pol["web"] as JsonObject ?? new JsonObject();
            pol["web"] = web;

            var ssObj = web["safeSearch"] as JsonObject ?? new JsonObject();
            web["safeSearch"] = ssObj;

            if (safeSearchEnabled is not null) ssObj["enabled"] = safeSearchEnabled.Value;
            if (youtubeRestrictedModeEnabled is not null) ssObj["youtubeRestrictedModeEnabled"] = youtubeRestrictedModeEnabled.Value;

            // Keep the provider field stable for forward-compat (stub semantics).
            if (!ssObj.ContainsKey("provider")) ssObj["provider"] = "stub";

            cp.UpsertLocalSettingsProfileJson(id, node.ToJsonString(JsonDefaults.Options));
        }
    }
    catch
    {
        // best-effort only; never fail the v1 write
    }

    return Results.Json(new ApiResponse<ChildPolicy>(updated, null), JsonDefaults.Options);
});

// 16W23: server-guided rollback to last-known-good policy snapshot.
// NOTE: route template uses {childId:guid} token; braces must be escaped in interpolated strings.
app.MapPost($"/api/{ApiVersions.V1}/children/{{childId:guid}}/policy/rollback-last-known-good", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    string? updatedBy = null;
    try
    {
        using var doc = await JsonDocument.ParseAsync(req.Body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("updatedBy", out var ub) && ub.ValueKind == JsonValueKind.String)
            updatedBy = ub.GetString();
    }
    catch { }

    if (!cp.TryRollbackPolicyToLastKnownGood(id, updatedBy, out var rolled, out var err))
    {
        return Results.Json(new ApiResponse<bool>(false, new ApiError("rollback_failed", err ?? "rollback_failed")), JsonDefaults.Options, statusCode: StatusCodes.Status409Conflict);
    }

    return Results.Json(new ApiResponse<bool>(rolled, null), JsonDefaults.Options);
});


// --- Local Mode API (Control Plane SSOT) ---
// vNext route surface for Parent App + Child App local communication.
// Backed by the same JsonFileControlPlane store to avoid duplicate registries.

var local = app.MapGroup("/api/local");

// Policy schema guardrail: provide a cheap, additive validation surface so we can harden the
// Parent→SSOT→Kid loop without breaking marker tests or existing endpoints.
// This does NOT mutate SSOT; it validates the *effective merged* policy surface (defaults + stored).
static List<string> ValidatePolicySurface(JsonNode? policy)
{
    var issues = new List<string>();
    if (policy is not JsonObject root)
    {
        issues.Add("policy.root.not_object");
        return issues;
    }

    static bool IsObj(JsonNode? n) => n is JsonObject;
    static bool IsArr(JsonNode? n) => n is JsonArray;
    static bool TryInt(JsonNode? n, out int v)
    {
        v = 0;
        if (n is not JsonValue jv) return false;
        try { v = jv.GetValue<int>(); return true; } catch { return false; }
    }

    // Required top-level objects
    if (!IsObj(root["timeBudget"])) issues.Add("policy.timeBudget.missing");
    if (!IsObj(root["apps"])) issues.Add("policy.apps.missing");
    if (!IsObj(root["web"])) issues.Add("policy.web.missing");
    if (!IsObj(root["location"])) issues.Add("policy.location.missing");
    if (!IsObj(root["alerts"])) issues.Add("policy.alerts.missing");

    // Time budget specifics (additive fields; tolerate absence but validate if present)
    if (root["timeBudget"] is JsonObject tb)
    {
        if (tb.TryGetPropertyValue("graceMinutes", out var gm) && gm is not null)
        {
            if (!TryInt(gm, out var g) || g < 0 || g > 120) issues.Add("policy.timeBudget.graceMinutes.invalid");
        }
        if (tb.TryGetPropertyValue("warnAtMinutes", out var wam) && wam is not null)
        {
            if (wam is not JsonArray arr) issues.Add("policy.timeBudget.warnAtMinutes.not_array");
            else
            {
                foreach (var item in arr)
                {
                    if (!TryInt(item, out var w) || w < 0 || w > 240) { issues.Add("policy.timeBudget.warnAtMinutes.invalid_item"); break; }
                }
            }
        }
    }

    // Web SafeSearch shape: allow bool (legacy) or object (expanded)
    if (root["web"] is JsonObject web && web.TryGetPropertyValue("safeSearch", out var ss) && ss is not null)
    {
        if (ss is JsonValue)
        {
            // bool is fine
        }
        else if (ss is JsonObject ssObj)
        {
            if (ssObj.TryGetPropertyValue("enabled", out var en) && en is not null && en is not JsonValue)
                issues.Add("policy.web.safeSearch.enabled.invalid");
            if (ssObj.TryGetPropertyValue("youtubeRestrictedModeEnabled", out var y) && y is not null && y is not JsonValue)
                issues.Add("policy.web.safeSearch.youtubeRestrictedModeEnabled.invalid");
        }
        else
        {
            issues.Add("policy.web.safeSearch.invalid_shape");
        }
    }

    // Location geofences
    if (root["location"] is JsonObject loc)
    {
        if (loc.TryGetPropertyValue("geofences", out var gf) && gf is not null && !IsArr(gf))
            issues.Add("policy.location.geofences.not_array");
    }

    // Alerts routing
    if (root["alerts"] is JsonObject al && al.TryGetPropertyValue("routing", out var rt) && rt is not null && !IsObj(rt))
        issues.Add("policy.alerts.routing.not_object");

    return issues;
}

// Anti-regression probe: lightweight liveness + SSOT smoke check.
// Keep the payload small and stable so UI self-test can rely on it.
local.MapGet("/_health", (JsonFileControlPlane cp) =>
{
    try
    {
        // Touch the store in a read-only way. If the JSON is corrupted or locking is broken,
        // this will throw and the probe will surface it.
        _ = cp.GetChildrenWithArchiveState(includeArchived: true).Take(1).ToList();
        return Results.Json(new { ok = true, atUtc = DateTimeOffset.UtcNow }, JsonDefaults.Options);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message, atUtc = DateTimeOffset.UtcNow }, JsonDefaults.Options,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

// Policy validation surface (read-only): checks the merged policy surface (defaults + stored) for each child.
// Additive-only endpoint used for milestone gating and regression isolation.
local.MapGet("/policy/validate", (JsonFileControlPlane cp) =>
{
    try
    {
        var rows = new List<object>();
        foreach (var c in cp.GetChildrenWithArchiveState(includeArchived: false))
        {
            var id = new ChildId(c.Id);
            var profileJson = cp.GetOrCreateLocalSettingsProfileJson(id);
            var (pv, eff, pol) = ReadPolicyEnvelopeFromProfileJson(id.Value.ToString(), profileJson);
            var issues = ValidatePolicySurface(pol);

            rows.Add(new
            {
                childId = id.Value,
                policyVersion = pv,
                effectiveAtUtc = eff,
                ok = issues.Count == 0,
                issues
            });
        }

        return Results.Json(new
        {
            ok = true,
            atUtc = DateTimeOffset.UtcNow,
            children = rows
        }, JsonDefaults.Options);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message, atUtc = DateTimeOffset.UtcNow }, JsonDefaults.Options,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

// SSOT diagnostics (read-only): used by DevTools + UI self-test.
// Keep additive-only and stable.
local.MapGet("/control-plane/info", (JsonFileControlPlane cp) =>
{
    try
    {
        // Keep this endpoint resilient to minor refactors in the control-plane info type.
        // (Some builds may return an anonymous/boxed object.) Use reflection + safe conversions.
        static object? ReadProp(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o);
        // Note: Nullable primitives box as their underlying type when they have a value.
        // When null, they box as null. So patterns like `v is bool?` are illegal and unnecessary.
        static bool ReadBool(object? v) => v is bool b ? b : (v is string s && bool.TryParse(s, out var p) ? p : false);
        static int ReadInt(object? v) => v is int i ? i : (v is string s && int.TryParse(s, out var p) ? p : 0);
        static string? ReadStr(object? v) => v as string;

        var infoObj = (object)cp.GetInfo();
        var ok = ReadBool(ReadProp(infoObj, "Ok"));
        var backend = ReadStr(ReadProp(infoObj, "Backend")) ?? "unknown";
        var schemaVersion = ReadInt(ReadProp(infoObj, "SchemaVersion"));
        var storagePath = ReadStr(ReadProp(infoObj, "StoragePath"));

        return Results.Json(new
        {
            ok,
            backend,
            schemaVersion,
            storagePath,
            atUtc = DateTimeOffset.UtcNow,
        }, JsonDefaults.Options);
    }
    catch (Exception ex)
    {
        return Results.Json(new
        {
            ok = false,
            error = ex.Message,
            atUtc = DateTimeOffset.UtcNow,
        }, JsonDefaults.Options, statusCode: StatusCodes.Status500InternalServerError);
    }
});

// Diagnostics bundle (read-only): support-ready payload for when UI is broken and you can only curl the server.
// Additive-only and best-effort; never throws.
local.MapGet("/diag/bundle", (HttpRequest req, JsonFileControlPlane cp) =>
{
    try
    {
        var atUtc = DateTimeOffset.UtcNow;

        var (hcOk, hcErr) = cp.TryHealthCheck();
        var cpInfoObj = (object)cp.GetInfo();
        static object? ReadProp(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o);
        // Nullable primitives box as their underlying type when they have a value; when null they box as null.
        // Avoid illegal patterns like `v is bool?`.
        static bool ReadBool(object? v) => v is bool b ? b : (v is string s && bool.TryParse(s, out var p) ? p : false);
        static int ReadInt(object? v) => v is int i ? i : (v is string s && int.TryParse(s, out var p) ? p : 0);
        static string? ReadStr(object? v) => v as string;

        // Keep stats cheap and read-only. Any exceptions are caught by outer try/catch.
        var children = cp.GetChildrenWithArchiveState(includeArchived: true).ToList();
        var activeChildren = children.Count(c => !c.Archived);
        var archivedChildren = children.Count(c => c.Archived);

        var devices = children.SelectMany(c => cp.GetDevices(new ChildId(c.Id))).ToList();
        var allReq = children.SelectMany(c => cp.GetRequests(new ChildId(c.Id), null, 200)).ToList();

        // Additive: policy surface validation summary (merged defaults + stored policy).
        var checkedChildren = 0;
        var childrenWithIssues = 0;
        foreach (var c in children.Where(c => !c.Archived))
        {
            checkedChildren++;
            var id = new ChildId(c.Id);
            var profileJson = cp.GetOrCreateLocalSettingsProfileJson(id);
            var (_, _, pol) = ReadPolicyEnvelopeFromProfileJson(id.Value.ToString(), profileJson);
            if (ValidatePolicySurface(pol).Count > 0) childrenWithIssues++;
        }

        var asm = typeof(Program).Assembly;
        var ver = asm.GetName().Version?.ToString() ?? "unknown";

        return Results.Json(new
        {
            ok = hcOk,
            service = "Safe0ne.DashboardServer",
            version = ver,
            atUtc,
            request = new
            {
                method = req.Method,
                path = req.Path.ToString(),
                remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString(),
            },
            environment = new
            {
                aspnetcoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown",
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                processArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            },
            health = new { ok = hcOk, error = hcErr },
            controlPlane = new
            {
                ok = ReadBool(ReadProp(cpInfoObj, "Ok")),
                backend = ReadStr(ReadProp(cpInfoObj, "Backend")) ?? "unknown",
                schemaVersion = ReadInt(ReadProp(cpInfoObj, "SchemaVersion")),
                storagePath = ReadStr(ReadProp(cpInfoObj, "StoragePath"))
            },
            stats = new
            {
                childrenTotal = children.Count,
                childrenActive = activeChildren,
                childrenArchived = archivedChildren,
                devicesTotal = devices.Count,
                requestsSampledTotal = allReq.Count
            },
            policyValidation = new
            {
                childrenChecked = checkedChildren,
                childrenWithIssues
            }
        }, JsonDefaults.Options);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message, atUtc = DateTimeOffset.UtcNow }, JsonDefaults.Options,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

// Legacy alias for tool compatibility (read-only).
app.MapGet($"/api/{ApiVersions.V1}/diag/bundle", (HttpRequest req, JsonFileControlPlane cp) =>
{
    // Delegate to the same implementation (keep response shape stable).
    // Note: we don't redirect to avoid CORS surprises in local tooling.
    var atUtc = DateTimeOffset.UtcNow;
    try
    {
        var (hcOk, hcErr) = cp.TryHealthCheck();
        var cpInfoObj = (object)cp.GetInfo();
        static object? ReadProp(object o, string name) => o.GetType().GetProperty(name)?.GetValue(o);
        static bool ReadBool(object? v) => v is bool b ? b : (v is string s && bool.TryParse(s, out var p) ? p : false);
        static int ReadInt(object? v) => v is int i ? i : (v is string s && int.TryParse(s, out var p) ? p : 0);
        static string? ReadStr(object? v) => v as string;

        var children = cp.GetChildrenWithArchiveState(includeArchived: true).ToList();
        var activeChildren = children.Count(c => !c.Archived);
        var archivedChildren = children.Count(c => c.Archived);

        var devices = children.SelectMany(c => cp.GetDevices(new ChildId(c.Id))).ToList();
        var allReq = children.SelectMany(c => cp.GetRequests(new ChildId(c.Id), null, 200)).ToList();

        // Additive: policy surface validation summary (merged defaults + stored policy).
        var checkedChildren = 0;
        var childrenWithIssues = 0;
        foreach (var c in children.Where(c => !c.Archived))
        {
            checkedChildren++;
            var id = new ChildId(c.Id);
            var profileJson = cp.GetOrCreateLocalSettingsProfileJson(id);
            var (_, _, pol) = ReadPolicyEnvelopeFromProfileJson(id.Value.ToString(), profileJson);
            if (ValidatePolicySurface(pol).Count > 0) childrenWithIssues++;
        }

        var asm = typeof(Program).Assembly;
        var ver = asm.GetName().Version?.ToString() ?? "unknown";

        return Results.Json(new
        {
            ok = hcOk,
            service = "Safe0ne.DashboardServer",
            version = ver,
            atUtc,
            request = new { method = req.Method, path = req.Path.ToString(), remoteIp = req.HttpContext.Connection.RemoteIpAddress?.ToString() },
            environment = new
            {
                aspnetcoreEnvironment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "unknown",
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                processArch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
                framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            },
            health = new { ok = hcOk, error = hcErr },
            controlPlane = new
            {
                ok = ReadBool(ReadProp(cpInfoObj, "Ok")),
                backend = ReadStr(ReadProp(cpInfoObj, "Backend")) ?? "unknown",
                schemaVersion = ReadInt(ReadProp(cpInfoObj, "SchemaVersion")),
                storagePath = ReadStr(ReadProp(cpInfoObj, "StoragePath"))
            },
            stats = new { childrenTotal = children.Count, childrenActive = activeChildren, childrenArchived = archivedChildren, devicesTotal = devices.Count, requestsSampledTotal = allReq.Count },
            policyValidation = new { childrenChecked = checkedChildren, childrenWithIssues }
        }, JsonDefaults.Options);
    }
    catch (Exception ex)
    {
        return Results.Json(new { ok = false, error = ex.Message, atUtc }, JsonDefaults.Options, statusCode: StatusCodes.Status500InternalServerError);
    }
});



// Children
local.MapGet("/children", (HttpRequest req, JsonFileControlPlane cp) =>
{
    var includeArchived = true;
    if (req.Query.TryGetValue("includeArchived", out var v))
    {
        _ = bool.TryParse(v.ToString(), out includeArchived);
    }

    var data = cp.GetChildrenWithArchiveState(includeArchived).ToList();
    return Results.Json(new ApiResponse<List<JsonFileControlPlane.LocalChildSnapshot>>(data, null), JsonDefaults.Options);
});

local.MapPost("/children", async (HttpRequest req, JsonFileControlPlane cp) =>
{
    var body = await req.ReadFromJsonAsync<CreateLocalChildRequest>(JsonDefaults.Options);
    var displayName = body?.DisplayName;
    if (string.IsNullOrWhiteSpace(displayName))
    {
        // LEGACY-COMPAT: older clients used { name: "..." }.
        displayName = body?.Name;
    }

    if (body is null || string.IsNullOrWhiteSpace(displayName))
    {
        return Results.Json(
            new ApiResponse<JsonFileControlPlane.LocalChildSnapshot>(null, new ApiError("bad_request", "DisplayName (or name) is required")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    var created = cp.CreateChild(displayName!);

    // Persist local UI metadata (gender/ageGroup/avatar) if provided.
    if (body.Gender is not null || body.AgeGroup is not null || body.Avatar is not null)
    {
        var meta = new
        {
            gender = body.Gender,
            ageGroup = body.AgeGroup,
            avatar = body.Avatar
        };
        cp.UpsertLocalChildMetaJson(created.Id, JsonSerializer.Serialize(meta, JsonDefaults.Options));
    }

    var snap = cp.GetChildrenWithArchiveState(includeArchived: true)
        .First(s => s.Id == created.Id.Value);

    return Results.Json(new ApiResponse<JsonFileControlPlane.LocalChildSnapshot>(snap, null), JsonDefaults.Options);
});


local.MapMethods("/children/{childId:guid}", new[] { "PATCH" }, async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var body = await req.ReadFromJsonAsync<PatchLocalChildRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.Json(
            new ApiResponse<JsonFileControlPlane.LocalChildSnapshot>(null, new ApiError("bad_request", "Missing or invalid JSON body")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    try
    {
        // Update name + archive state using the SSOT primitives.
        cp.UpdateChild(id, body.DisplayName, body.Archived);

        if (body.Gender is not null || body.AgeGroup is not null || body.Avatar is not null)
        {
            var current = cp.GetChildrenWithArchiveState(includeArchived: true).FirstOrDefault(c => c.Id == id.Value);

            static LocalAvatar? TryReadAvatar(object? raw)
            {
                if (raw is null) return null;
                try
                {
                    // raw may already be a LocalAvatar, a JsonElement, or a boxed primitive/string.
                    if (raw is LocalAvatar a) return a;
                    if (raw is System.Text.Json.JsonElement el)
                    {
                        return el.ValueKind == System.Text.Json.JsonValueKind.Undefined || el.ValueKind == System.Text.Json.JsonValueKind.Null
                            ? null
                            : System.Text.Json.JsonSerializer.Deserialize<LocalAvatar>(el, JsonDefaults.Options);
                    }
                    var s = raw.ToString();
                    if (string.IsNullOrWhiteSpace(s)) return null;
                    return System.Text.Json.JsonSerializer.Deserialize<LocalAvatar>(s, JsonDefaults.Options);
                }
                catch
                {
                    return null;
                }
            }

            var meta = new
            {
                gender = body.Gender ?? current?.Gender,
                ageGroup = body.AgeGroup ?? current?.AgeGroup,
                avatar = body.Avatar ?? TryReadAvatar(current?.Avatar)
            };

            cp.UpsertLocalChildMetaJson(id, JsonSerializer.Serialize(meta, JsonDefaults.Options));
        }

        var snap = cp.GetChildrenWithArchiveState(includeArchived: true).First(s => s.Id == childId);
        return Results.Json(new ApiResponse<JsonFileControlPlane.LocalChildSnapshot>(snap, null), JsonDefaults.Options);
    }
    catch (InvalidOperationException)
    {
        return Results.Json(
            new ApiResponse<JsonFileControlPlane.LocalChildSnapshot>(null, new ApiError("not_found", "Child not found")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status404NotFound);
    }
});



// Profile / Policy
local.MapGet("/children/{childId:guid}/profile", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var json = cp.GetOrCreateLocalSettingsProfileJson(id);

    using var doc = JsonDocument.Parse(json);
    var payload = doc.RootElement.Clone();
    return Results.Json(new ApiResponse<JsonElement>(payload, null), JsonDefaults.Options);
});


local.MapPut("/children/{childId:guid}/profile", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    JsonElement body;
    try
    {
        body = (await req.ReadFromJsonAsync<JsonElement>(JsonDefaults.Options))!;
    }
    catch
    {
        return Results.Json(
            new ApiResponse<JsonElement>(default, new ApiError("bad_request", "Missing or invalid JSON body")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    var json = JsonSerializer.Serialize(body, JsonDefaults.Options);
    cp.UpsertLocalSettingsProfileJson(id, json);

    return Results.Json(new ApiResponse<JsonElement>(body, null), JsonDefaults.Options);
});



// Local Policy Envelope (Phase A stubs)
// Purpose: give the Kid a stable pollable policy surface with versioning, without breaking v1 contracts.
// Source of truth is the Local Settings Profile JSON stored in the Control Plane (SSOT).
static JsonObject BuildDefaultPolicySurface()
{
    // Additive-only. Keep defaults conservative and easy to evolve.
    return new JsonObject
    {
        ["mode"] = "Open", // Open | Homework | Bedtime | Lockdown
        ["timeBudget"] = new JsonObject
        {
            ["dailyMinutes"] = 120,
            // PATCH 16R: grace period + warning thresholds (additive)
            ["graceMinutes"] = 0,
            ["warnAtMinutes"] = new JsonArray(5, 1),
            // Legacy keys (kept for backward compat with older builds)
            ["warningMinutesRemaining"] = 15,
            ["hardStopMinutesRemaining"] = 0,
            ["schedules"] = new JsonObject
            {
                ["bedtime"] = new JsonObject { ["start"] = "21:00", ["end"] = "07:00" },
                ["school"] = new JsonObject { ["start"] = "09:00", ["end"] = "15:30" }
            }
        },
        ["routines"] = new JsonObject
        {
            ["templates"] = new JsonArray(
                new JsonObject { ["id"] = "bedtime", ["name"] = "Bedtime" },
                new JsonObject { ["id"] = "school", ["name"] = "School" }
            )
        },
        ["apps"] = new JsonObject
        {
            ["blockNewApps"] = false,
            ["allow"] = new JsonArray(),
            ["deny"] = new JsonArray(),
            ["limits"] = new JsonArray() // [{ appId, dailyMinutes }]
        },
        ["web"] = new JsonObject
        {
            ["safeSearch"] = new JsonObject { ["enabled"] = false },
            ["categories"] = new JsonObject
            {
                ["allow"] = new JsonArray(),
                ["alert"] = new JsonArray(),
                ["block"] = new JsonArray()
            },
            ["domains"] = new JsonObject
            {
                ["allow"] = new JsonArray(),
                ["deny"] = new JsonArray()
            }
        },
        ["exceptions"] = new JsonArray(), // [{ kind, target, untilUtc, note }]
        ["alwaysAllowed"] = new JsonObject
        {
            ["apps"] = new JsonArray(),
            ["sites"] = new JsonArray(),
            ["contacts"] = new JsonArray()
        },
        ["location"] = new JsonObject
        {
            ["sharingEnabled"] = false,
            ["geofences"] = new JsonArray() // stubs
        },
        ["alerts"] = new JsonObject
        {
            ["enabled"] = true,
            ["thresholds"] = new JsonObject
            {
                ["timeBudget"] = true,
                ["blockedAppLaunch"] = false,
                ["blockedSiteVisit"] = false,
                ["geofence"] = false
            }
        },
        // Reports scheduling (Phase 16W7): additive stub config persisted in SSOT policy.
        // NOTE: Scheduling execution (timers, delivery) is out of scope here; this is authoring + persistence.
        ["reports"] = new JsonObject
        {
            ["enabled"] = false,
            ["digest"] = new JsonObject
            {
                // off | daily | weekly
                ["frequency"] = "off",
                // HH:mm (24h)
                ["timeLocal"] = "18:00",
                // mon..sun (only used when frequency=weekly)
                ["weekday"] = "sun"
            }
        }
    };
}

static (int policyVersion, string? effectiveAtUtc, JsonNode policy) ReadPolicyEnvelopeFromProfileJson(string childId, string profileJson)
{
    JsonNode? root;
    try
    {
        root = JsonNode.Parse(profileJson);
    }
    catch
    {
        root = null;
    }

    var obj = root as JsonObject ?? new JsonObject();

    // Root versioning fields (additive)
    var pv = 1;
    if (obj.TryGetPropertyValue("policyVersion", out var pvNode) && pvNode is JsonValue)
    {
        try { pv = pvNode.GetValue<int>(); } catch { /* keep default */ }
    }

    string? eff = null;
    if (obj.TryGetPropertyValue("effectiveAtUtc", out var effNode) && effNode is JsonValue)
    {
        try { eff = effNode.GetValue<string?>(); } catch { eff = null; }
    }

    // Policy surface: deep-merge defaults into stored policy (without persisting).
    JsonObject defaults = BuildDefaultPolicySurface();
    JsonObject storedPolicy = new JsonObject();
    if (obj.TryGetPropertyValue("policy", out var pNode) && pNode is JsonObject po)
    {
        storedPolicy = po;
    }

    // Legacy migration (best-effort): map old limits/permissions into the new surface.
    // - limits.screenMinutesPerDay -> policy.timeBudget.dailyMinutes
    // - limits.bedtimeStart/End -> policy.timeBudget.schedules.bedtime
    if (obj.TryGetPropertyValue("limits", out var limitsNode) && limitsNode is JsonObject lim)
    {
        if (lim.TryGetPropertyValue("screenMinutesPerDay", out var sm) && sm is JsonValue)
        {
            try
            {
                var v = sm.GetValue<int>();
                if (!storedPolicy.TryGetPropertyValue("timeBudget", out var tbNode) || tbNode is not JsonObject)
                    storedPolicy["timeBudget"] = new JsonObject();
                var tb = (JsonObject)storedPolicy["timeBudget"]!;
                if (!tb.TryGetPropertyValue("dailyMinutes", out var dm))
                    tb["dailyMinutes"] = v;
            }
            catch { }
        }

        if (lim.TryGetPropertyValue("bedtimeStart", out var bs) && bs is JsonValue &&
            lim.TryGetPropertyValue("bedtimeEnd", out var be) && be is JsonValue)
        {
            try
            {
                var start = bs.GetValue<string>();
                var end = be.GetValue<string>();
                if (!storedPolicy.TryGetPropertyValue("timeBudget", out var tbNode) || tbNode is not JsonObject)
                    storedPolicy["timeBudget"] = new JsonObject();
                var tb = (JsonObject)storedPolicy["timeBudget"]!;
                if (!tb.TryGetPropertyValue("schedules", out var schNode) || schNode is not JsonObject)
                    tb["schedules"] = new JsonObject();
                var sch = (JsonObject)tb["schedules"]!;
                if (!sch.TryGetPropertyValue("bedtime", out var btNode) || btNode is not JsonObject)
                    sch["bedtime"] = new JsonObject();
                var bt = (JsonObject)sch["bedtime"]!;
                if (!bt.TryGetPropertyValue("start", out _)) bt["start"] = start;
                if (!bt.TryGetPropertyValue("end", out _)) bt["end"] = end;
            }
            catch { }
        }
    }

    // PATCH 16R: legacy migration for warning thresholds.
    // If older profiles used policy.timeBudget.warnMinutesRemaining, map it to warnAtMinutes.
    try
    {
        if (storedPolicy.TryGetPropertyValue("timeBudget", out var tbNode2) && tbNode2 is JsonObject tb2)
        {
            if (!tb2.TryGetPropertyValue("warnAtMinutes", out var _) && tb2.TryGetPropertyValue("warnMinutesRemaining", out var legacy) && legacy is JsonArray legacyArr)
            {
                // Clone into a new array to avoid re-parenting exceptions.
                var cloned = new JsonArray();
                foreach (var item in legacyArr)
                {
                    cloned.Add(item?.DeepClone());
                }
                tb2["warnAtMinutes"] = cloned;
            }
        }
    }
    catch { /* best-effort */ }

    JsonNode DeepMerge(JsonNode def, JsonNode? src)
    {
        // IMPORTANT: JsonNode instances cannot be re-parented. Always clone nodes when carrying
        // them into a new tree, otherwise System.Text.Json will throw "The node already has a parent".
        if (src is null) return def.DeepClone();
        if (def is JsonArray)
        {
            // Arrays are treated as replace-or-default in this stub. Clone whichever wins.
            return (src is JsonArray ? src : def).DeepClone();
        }
        if (def is JsonObject dobj)
        {
            if (src is not JsonObject sobj) return src.DeepClone();
            var outObj = new JsonObject();
            foreach (var kv in dobj)
                outObj[kv.Key] = kv.Value is null ? null : DeepMerge(kv.Value, sobj.TryGetPropertyValue(kv.Key, out var sv) ? sv : null);
            foreach (var kv in sobj)
                if (!outObj.ContainsKey(kv.Key)) outObj[kv.Key] = kv.Value is null ? null : kv.Value.DeepClone();
            return outObj;
        }
        // primitives: prefer src
        return src.DeepClone();
    }

    var merged = (JsonObject)DeepMerge(defaults, storedPolicy);

    // Ensure childId is always echoed in the payload for convenience.
    merged["childId"] = childId;

    return (pv, eff, merged);
}

// GET /api/local/children/{id}/policy
local.MapGet("/children/{childId:guid}/policy", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var json = cp.GetOrCreateLocalSettingsProfileJson(id);
    var (pv, eff, pol) = ReadPolicyEnvelopeFromProfileJson(childId.ToString(), json);

    return Results.Json(new ApiResponse<object>(new
    {
        childId,
        policyVersion = pv,
        effectiveAtUtc = eff,
        policy = pol
    }, null), JsonDefaults.Options);
});

// GET /api/local/children/{id}/policy/effective
// For Phase A stubs, this returns the same policy surface with defaults applied.
local.MapGet("/children/{childId:guid}/policy/effective", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var json = cp.GetOrCreateLocalSettingsProfileJson(id);
    var (pv, eff, pol) = ReadPolicyEnvelopeFromProfileJson(childId.ToString(), json);

    // NOTE: JsonNode cannot appear twice in the same graph (parenting rules). Clone for effectivePolicy.
    var effPol = pol.DeepClone();

    return Results.Json(new ApiResponse<object>(new
    {
        childId,
        policyVersion = pv,
        effectiveAtUtc = eff,
        policy = pol,
        effectivePolicy = effPol
    }, null), JsonDefaults.Options);
});

// PUT /api/local/children/{id}/policy
// Body can be either a direct policy object OR { policy: { ... } } envelope.
// Persists into the Local Settings Profile JSON (SSOT) under the "policy" key and bumps policyVersion.
local.MapPut("/children/{childId:guid}/policy", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    string raw;
    using (var reader = new StreamReader(req.Body))
        raw = await reader.ReadToEndAsync();

    JsonNode? incomingRoot;
    try { incomingRoot = JsonNode.Parse(raw); } catch { incomingRoot = null; }
    if (incomingRoot is not JsonObject incomingObj)
    {
        return Results.Json(
            new ApiResponse<object>(null, new ApiError("bad_request", "Missing or invalid JSON body")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    // Accept either { policy: {...} } or direct {...}
    JsonNode? incomingPolicy = incomingObj;
    if (incomingObj.TryGetPropertyValue("policy", out var pNode) && pNode is JsonObject)
        incomingPolicy = pNode;

    if (incomingPolicy is not JsonObject)
    {
        return Results.Json(
            new ApiResponse<object>(null, new ApiError("bad_request", "Body must be a policy object or {policy:{...}}")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    // Update profile JSON
    var profileJson = cp.GetOrCreateLocalSettingsProfileJson(id);
    var profileNode = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject();

    profileNode["policy"] = incomingPolicy.DeepClone();
    var nextVersion = 1;
    if (profileNode.TryGetPropertyValue("policyVersion", out var pvNode) && pvNode is JsonValue)
    {
        try { nextVersion = pvNode.GetValue<int>() + 1; } catch { nextVersion = 1; }
    }
    profileNode["policyVersion"] = nextVersion;
    profileNode["effectiveAtUtc"] = DateTime.UtcNow.ToString("O");

    cp.UpsertLocalSettingsProfileJson(id, profileNode.ToJsonString(JsonDefaults.Options));

    // Return envelope
    var (pv, eff, pol) = ReadPolicyEnvelopeFromProfileJson(childId.ToString(), profileNode.ToJsonString(JsonDefaults.Options));
    return Results.Json(new ApiResponse<object>(new { childId, policyVersion = pv, effectiveAtUtc = eff, policy = pol }, null), JsonDefaults.Options);
});

// PATCH /api/local/children/{id}/policy
// Deep-merges provided fields into the stored policy (additive). Same body rules as PUT.
local.MapPatch("/children/{childId:guid}/policy", async (Guid childId, HttpRequest req, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);

    string raw;
    using (var reader = new StreamReader(req.Body))
        raw = await reader.ReadToEndAsync();

    JsonNode? incomingRoot;
    try { incomingRoot = JsonNode.Parse(raw); } catch { incomingRoot = null; }
    if (incomingRoot is not JsonObject incomingObj)
    {
        return Results.Json(
            new ApiResponse<object>(null, new ApiError("bad_request", "Missing or invalid JSON body")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    JsonNode? patchPolicy = incomingObj;
    if (incomingObj.TryGetPropertyValue("policy", out var pNode) && pNode is JsonObject)
        patchPolicy = pNode;

    if (patchPolicy is not JsonObject patchObj)
    {
        return Results.Json(
            new ApiResponse<object>(null, new ApiError("bad_request", "Body must be a policy object or {policy:{...}}")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    var profileJson = cp.GetOrCreateLocalSettingsProfileJson(id);
    var profileNode = JsonNode.Parse(profileJson) as JsonObject ?? new JsonObject();
    var storedPolicy = profileNode["policy"] as JsonObject ?? new JsonObject();

    static JsonNode DeepMerge(JsonNode dst, JsonNode src)
    {
        if (dst is JsonObject dobj && src is JsonObject sobj)
        {
            var o = (JsonObject)dobj.DeepClone();
            foreach (var kv in sobj)
            {
                if (kv.Value is null) { o[kv.Key] = null; continue; }
                if (o.TryGetPropertyValue(kv.Key, out var existing) && existing is not null)
                    o[kv.Key] = DeepMerge(existing, kv.Value);
                else
                    o[kv.Key] = kv.Value.DeepClone();
            }
            return o;
        }
        return src.DeepClone();
    }

    profileNode["policy"] = DeepMerge(storedPolicy, patchObj);
    var nextVersion = 1;
    if (profileNode.TryGetPropertyValue("policyVersion", out var pvNode) && pvNode is JsonValue)
    {
        try { nextVersion = pvNode.GetValue<int>() + 1; } catch { nextVersion = 1; }
    }
    profileNode["policyVersion"] = nextVersion;
    profileNode["effectiveAtUtc"] = DateTime.UtcNow.ToString("O");

    cp.UpsertLocalSettingsProfileJson(id, profileNode.ToJsonString(JsonDefaults.Options));

    var (pv, eff, pol) = ReadPolicyEnvelopeFromProfileJson(childId.ToString(), profileNode.ToJsonString(JsonDefaults.Options));
    return Results.Json(new ApiResponse<object>(new { childId, policyVersion = pv, effectiveAtUtc = eff, policy = pol }, null), JsonDefaults.Options);
});

// v1 aliases (additive): keep Kid/diagnostics flexible without breaking the existing /api/v1/.../policy contract.
app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/policy/envelope", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var json = cp.GetOrCreateLocalSettingsProfileJson(id);
    var (pv, eff, pol) = ReadPolicyEnvelopeFromProfileJson(childId.ToString(), json);

    return Results.Json(new ApiResponse<object>(new
    {
        childId,
        policyVersion = pv,
        effectiveAtUtc = eff,
        policy = pol
    }, null), JsonDefaults.Options);
});

app.MapGet($"/api/{ApiVersions.V1}/children/{{childId:guid}}/policy/effective", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var json = cp.GetOrCreateLocalSettingsProfileJson(id);
    var (pv, eff, pol) = ReadPolicyEnvelopeFromProfileJson(childId.ToString(), json);

    var effPol = pol.DeepClone();

    return Results.Json(new ApiResponse<object>(new
    {
        childId,
        policyVersion = pv,
        effectiveAtUtc = eff,
        policy = pol,
        effectivePolicy = effPol
    }, null), JsonDefaults.Options);
});


// Devices + enrollment
local.MapGet("/children/{childId:guid}/devices", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var devices = cp.GetDevices(id).ToList();
    return Results.Json(new ApiResponse<List<ChildDeviceSummary>>(devices, null), JsonDefaults.Options);
});

local.MapPost("/children/{childId:guid}/devices/pair", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var res = cp.StartPairing(id);
    return Results.Json(new ApiResponse<PairingStartResponse>(res, null), JsonDefaults.Options);
});

// Optional helper: allow the parent UI to query the currently active pairing session (if any).
local.MapGet("/children/{childId:guid}/devices/pairing", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    if (!cp.TryGetPendingPairing(id, out var resp))
    {
        return Results.Json(new ApiResponse<PairingStartResponse?>(null, null), JsonDefaults.Options);
    }

    return Results.Json(new ApiResponse<PairingStartResponse>(resp, null), JsonDefaults.Options);
});


// Parent can clear/cancel the current pairing session for a child.
local.MapDelete("/children/{childId:guid}/devices/pairing", (Guid childId, JsonFileControlPlane cp) =>
{
    var id = new ChildId(childId);
    var removed = cp.ClearPendingPairing(id);
    return Results.Json(new ApiResponse<object>(new { removed }, null), JsonDefaults.Options);
});

// Kid enrolls with pairing code (one-time) + device info.
local.MapPost("/devices/enroll", async (HttpRequest req, JsonFileControlPlane cp) =>
{
    var body = await req.ReadFromJsonAsync<PairingCompleteRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.Json(
            new ApiResponse<PairingCompleteResponse>(null, new ApiError("bad_request", "Missing or invalid JSON body")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    // Basic server-side validation: pairing code + device metadata must be present.
    if (string.IsNullOrWhiteSpace(body.PairingCode))
    {
        return Results.Json(
            new ApiResponse<PairingCompleteResponse>(null, new ApiError("bad_request", "PairingCode is required")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    if (!cp.TryCompletePairingByCode(body, out var resp))
    {
        return Results.Json(
            new ApiResponse<PairingCompleteResponse>(null, new ApiError("invalid_code", "Invalid or expired pairing code")),
            JsonDefaults.Options,
            statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Json(new ApiResponse<PairingCompleteResponse>(resp, null), JsonDefaults.Options);
});

// Unpair a device (parent action).
local.MapDelete("/devices/{deviceId:guid}", (Guid deviceId, JsonFileControlPlane cp) =>
{
    if (!cp.TryUnpairDevice(deviceId, out var childId))
    {
        return Results.Json(new ApiResponse<object?>(null, new ApiError("not_found", "Device not found")), JsonDefaults.Options, statusCode: StatusCodes.Status404NotFound);
    }

    return Results.Json(new ApiResponse<object>(new { ok = true, childId = childId.Value }, null), JsonDefaults.Options);
});

// Revoke a device token (parent action). Keeps the device record but makes auth fail.
local.MapPost("/devices/{deviceId:guid}/revoke", async (HttpRequest req, Guid deviceId, JsonFileControlPlane cp) =>
{
    RevokeDeviceTokenRequest? body = null;
    try
    {
        body = await req.ReadFromJsonAsync<RevokeDeviceTokenRequest>(JsonDefaults.Options);
    }
    catch
    {
        body = null;
    }

    var revokedBy = body?.RevokedBy ?? "parent";
    var reason = body?.Reason;

    if (!cp.TryRevokeDeviceToken(deviceId, revokedBy, reason, out var childId))
    {
        return Results.Json(new ApiResponse<object?>(null, new ApiError("not_found", "Device not found")), JsonDefaults.Options, statusCode: StatusCodes.Status404NotFound);
    }

	// NOTE: avoid naming pattern variables 'cid' because it can conflict with later locals in this lambda scope.
	return Results.Json(new ApiResponse<object>(new { ok = true, childId = (childId is { } childIdVal ? (Guid?)childIdVal.Value : null) }, null), JsonDefaults.Options);
});

// Requests / Activity / Location (Local Mode)

// Requests
local.MapGet("/children/{childId:guid}/requests", (HttpRequest req, Guid childId, JsonFileControlPlane cp) =>
{
    var take = 200;
    if (req.Query.TryGetValue("take", out var t) && int.TryParse(t.ToString(), out var n))
    {
        take = n;
    }

    // Optional status filter
    AccessRequestStatus? status = null;
    if (req.Query.TryGetValue("status", out var s) && Enum.TryParse<AccessRequestStatus>(s.ToString(), true, out var parsed))
    {
        status = parsed;
    }

    var data = cp.GetRequests(new ChildId(childId), status, take).ToList();
    return Results.Json(new ApiResponse<List<AccessRequest>>(data, null), JsonDefaults.Options);
});

local.MapPost("/children/{childId:guid}/requests", async (HttpRequest req, Guid childId, JsonFileControlPlane cp) =>
{
    var body = await req.ReadFromJsonAsync<CreateAccessRequestRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<object>(null, new ApiError("invalid_body", "Missing request body.")));
    }

    var created = cp.CreateOrGetRequest(new ChildId(childId), body);
    return Results.Json(new ApiResponse<AccessRequest>(created, null), JsonDefaults.Options);
});

local.MapPost("/children/{childId:guid}/requests/{reqId:guid}/decision", async (HttpRequest req, Guid childId, Guid reqId, JsonFileControlPlane cp) =>
{
    var body = await req.ReadFromJsonAsync<DecideAccessRequestRequest>(JsonDefaults.Options);
    if (body is null)
    {
        return Results.BadRequest(new ApiResponse<object>(null, new ApiError("invalid_body", "Missing decision body.")));
    }

    if (!cp.TryDecideRequest(reqId, body, out var updated, out var grant))
    {
        return Results.NotFound(new ApiResponse<object>(null, new ApiError("not_found", "Request not found.")));
    }

    return Results.Json(new ApiResponse<object>(new { request = updated, grant }, null), JsonDefaults.Options);
});
local.MapGet("/children/{childId:guid}/activity", (HttpRequest req, Guid childId, JsonFileControlPlane cp) =>
{
    DateTimeOffset? from = null;
    DateTimeOffset? to = null;
    int take = 200;
    if (req.Query.TryGetValue("from", out var fromV) && DateTimeOffset.TryParse(fromV.ToString(), out var f)) from = f;
    if (req.Query.TryGetValue("to", out var toV) && DateTimeOffset.TryParse(toV.ToString(), out var t)) to = t;
    if (req.Query.TryGetValue("take", out var takeV) && int.TryParse(takeV.ToString(), out var tk)) take = tk;

    var json = cp.GetLocalActivityJson(new ChildId(childId), from, to, take);
    using var doc = JsonDocument.Parse(json);
    var data = doc.RootElement.Clone();
    return Results.Json(new ApiResponse<JsonElement>(data, null), JsonDefaults.Options);
});

local.MapPost("/children/{childId:guid}/activity", async (HttpRequest req, Guid childId, JsonFileControlPlane cp) =>
{
    // Accept either { events: [...] } or a raw array [...]
    using var bodyDoc = await JsonDocument.ParseAsync(req.Body);
    JsonElement root = bodyDoc.RootElement;
    JsonElement array;
    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("events", out var ev) && ev.ValueKind == JsonValueKind.Array)
    {
        array = ev;
    }
    else if (root.ValueKind == JsonValueKind.Array)
    {
        array = root;
    }
    else
    {
        return Results.BadRequest(new ApiResponse<object>(null, new ApiError("invalid_body", "Expected an array of activity events.")));
    }

    // Store as JSON array string.
    var json = array.GetRawText();
    cp.AppendLocalActivityJson(new ChildId(childId), json);
    return Results.Json(new ApiResponse<object>(new { ok = true }, null), JsonDefaults.Options);
});

// Export a stable activity envelope for diagnostics / support bundles.
// Returns the retained activity slice (up to retention max) in a single response.
local.MapGet("/children/{childId:guid}/activity/export", (Guid childId, JsonFileControlPlane cp) =>
{
    // Export the full retained window (SSOT retention is 2000 events / 30 days).
    var json = cp.GetLocalActivityJson(new ChildId(childId), fromUtc: null, toUtc: null, take: 2000);
    using var doc = JsonDocument.Parse(json);
    var events = doc.RootElement.Clone();
    var data = new
    {
        childId,
        exportedAtUtc = DateTimeOffset.UtcNow,
        events
    };
    return Results.Json(new ApiResponse<object>(data, null), JsonDefaults.Options);
});


// 16W9: Reports schedule authoring + run-now (Local Mode).
// Stored under Local Settings Profile policy.reports; execution state stored under root.reportsState.
local.MapGet("/children/{childId:guid}/reports/schedule", (Guid childId, JsonFileControlPlane cp) =>
{
    var env = ReportsDigest.ReadScheduleEnvelope(cp, new ChildId(childId), DateTimeOffset.UtcNow);
    return Results.Json(new ApiResponse<object>(env, null), JsonDefaults.Options);
});

local.MapPut("/children/{childId:guid}/reports/schedule", async (HttpRequest req, Guid childId, JsonFileControlPlane cp) =>
{
    JsonObject? patch = null;
    try
    {
        patch = await req.ReadFromJsonAsync<JsonObject>(JsonDefaults.Options);
    }
    catch { }

    if (patch is null)
    {
        return Results.Json(new ApiResponse<object?>(null, new ApiError("invalid_body", "Expected JSON object schedule patch.")),
            JsonDefaults.Options, statusCode: StatusCodes.Status400BadRequest);
    }

    ReportsDigest.UpsertSchedule(cp, new ChildId(childId), patch);
    var env = ReportsDigest.ReadScheduleEnvelope(cp, new ChildId(childId), DateTimeOffset.UtcNow);
    return Results.Json(new ApiResponse<object>(env, null), JsonDefaults.Options);
});

local.MapPost("/children/{childId:guid}/reports/run-now", (Guid childId, JsonFileControlPlane cp) =>
{
    var ran = ReportsDigest.TryRunDigestIfDue(cp, new ChildId(childId), DateTimeOffset.UtcNow, force: true);
    return Results.Json(new ApiResponse<object>(new { ran }, null), JsonDefaults.Options);
});


local.MapGet("/children/{childId:guid}/location", (Guid childId, JsonFileControlPlane cp) =>
{
    var json = cp.GetLocalLocationJson(new ChildId(childId));
    using var doc = JsonDocument.Parse(json);
    var data = doc.RootElement.Clone();
    return Results.Json(new ApiResponse<JsonElement>(data, null), JsonDefaults.Options);
});

local.MapPost("/children/{childId:guid}/location", async (HttpRequest req, Guid childId, JsonFileControlPlane cp) =>
{
    // Accept either { location: {...} } or a raw object {...}
    using var bodyDoc = await JsonDocument.ParseAsync(req.Body);
    JsonElement root = bodyDoc.RootElement;
    JsonElement obj;
    if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("location", out var loc) && loc.ValueKind == JsonValueKind.Object)
    {
        obj = loc;
    }
    else if (root.ValueKind == JsonValueKind.Object)
    {
        obj = root;
    }
    else
    {
        return Results.Json(new ApiResponse<object?>(null, new ApiError("invalid_body", "Expected a JSON object for location.")), JsonDefaults.Options, statusCode: StatusCodes.Status400BadRequest);
    }

    var json = obj.GetRawText();
    cp.UpsertLocalLocationJson(new ChildId(childId), json);

    // Phase C (15E): Evaluate configured geofences (if any) and emit enter/exit Activity events.
    // This is best-effort and must never break the existing location flow.
    try
    {
        if (TryExtractLatLon(obj, out var lat, out var lon))
        {
            var profileJson = cp.GetOrCreateLocalSettingsProfileJson(new ChildId(childId));
            if (TryEvaluateGeofencesAndUpdateState(profileJson, lat, lon, out var updatedProfileJson, out var geofenceEventsJsonArray)
                && !string.IsNullOrWhiteSpace(updatedProfileJson))
            {
                cp.UpsertLocalSettingsProfileJson(new ChildId(childId), updatedProfileJson);
            }

            if (!string.IsNullOrWhiteSpace(geofenceEventsJsonArray) && geofenceEventsJsonArray != "[]")
            {
                cp.AppendLocalActivityJson(new ChildId(childId), geofenceEventsJsonArray);
            }
        }
    }
    catch
    {
        // swallow (no regressions)
    }

    using var outDoc = JsonDocument.Parse(json);
    var payload = outDoc.RootElement.Clone();
    return Results.Json(new ApiResponse<JsonElement>(payload, null), JsonDefaults.Options);
});

static bool TryExtractLatLon(JsonElement locationObj, out double lat, out double lon)
{
    lat = 0;
    lon = 0;

    bool gotLat = false, gotLon = false;
    if (locationObj.TryGetProperty("latitude", out var la) && la.ValueKind == JsonValueKind.Number && la.TryGetDouble(out var lad))
    {
        lat = lad; gotLat = true;
    }
    else if (locationObj.TryGetProperty("lat", out var la2) && la2.ValueKind == JsonValueKind.Number && la2.TryGetDouble(out var lad2))
    {
        lat = lad2; gotLat = true;
    }

    if (locationObj.TryGetProperty("longitude", out var lo) && lo.ValueKind == JsonValueKind.Number && lo.TryGetDouble(out var lod))
    {
        lon = lod; gotLon = true;
    }
    else if (locationObj.TryGetProperty("lon", out var lo2) && lo2.ValueKind == JsonValueKind.Number && lo2.TryGetDouble(out var lod2))
    {
        lon = lod2; gotLon = true;
    }

    return gotLat && gotLon && !double.IsNaN(lat) && !double.IsNaN(lon);
}

static bool TryEvaluateGeofencesAndUpdateState(string profileJson, double lat, double lon, out string updatedProfileJson, out string geofenceEventsJsonArray)
{
    updatedProfileJson = profileJson;
    geofenceEventsJsonArray = "[]";

    System.Text.Json.Nodes.JsonNode? node;
    try
    {
        node = System.Text.Json.Nodes.JsonNode.Parse(profileJson);
    }
    catch
    {
        return false;
    }

    if (node is not System.Text.Json.Nodes.JsonObject root)
        return false;

    // Read policy.location.geofences
    var geofenceSpecs = new List<(string Id, string? Name, double Lat, double Lon, double RadiusM)>();
    try
    {
        var policy = root["policy"] as System.Text.Json.Nodes.JsonObject;
        var loc = policy?["location"] as System.Text.Json.Nodes.JsonObject;
        var fences = loc?["geofences"] as System.Text.Json.Nodes.JsonArray;
        if (fences is null || fences.Count == 0)
            return false;

        foreach (var item in fences)
        {
            if (item is null) continue;
            if (item is System.Text.Json.Nodes.JsonValue v)
            {
                if (v.TryGetValue<string>(out var s) && TryParseGeofenceString(s, out var spec))
                {
                    geofenceSpecs.Add(spec);
                }
                else if (v.TryGetValue<System.Text.Json.JsonElement>(out var je) && je.ValueKind == JsonValueKind.Object)
                {
                    if (TryParseGeofenceJsonElement(je, out var spec2)) geofenceSpecs.Add(spec2);
                }
            }
            else if (item is System.Text.Json.Nodes.JsonObject o)
            {
                // Serialize to element and parse
                var text = o.ToJsonString(JsonDefaults.Options);
                using var doc = JsonDocument.Parse(text);
                if (TryParseGeofenceJsonElement(doc.RootElement, out var spec3)) geofenceSpecs.Add(spec3);
            }
        }
    }
    catch
    {
        return false;
    }

    if (geofenceSpecs.Count == 0)
        return false;

    // Ensure locationState.geofences array exists
    if (root["locationState"] is not System.Text.Json.Nodes.JsonObject state)
    {
        state = new System.Text.Json.Nodes.JsonObject();
        root["locationState"] = state;
    }
    if (state["geofences"] is not System.Text.Json.Nodes.JsonArray stateArr)
    {
        stateArr = new System.Text.Json.Nodes.JsonArray();
        state["geofences"] = stateArr;
    }

    var byId = new Dictionary<string, System.Text.Json.Nodes.JsonObject>(StringComparer.OrdinalIgnoreCase);
    foreach (var n in stateArr)
    {
        if (n is System.Text.Json.Nodes.JsonObject so && so["id"] is System.Text.Json.Nodes.JsonValue idv && idv.TryGetValue<string>(out var idStr) && !string.IsNullOrWhiteSpace(idStr))
        {
            byId[idStr] = so;
        }
    }

    var events = new System.Text.Json.Nodes.JsonArray();
    var now = DateTimeOffset.UtcNow;

    foreach (var spec in geofenceSpecs)
    {
        var dist = HaversineMeters(lat, lon, spec.Lat, spec.Lon);
        var inside = dist <= spec.RadiusM;
        if (!byId.TryGetValue(spec.Id, out var st))
        {
            st = new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = spec.Id,
                ["inside"] = inside,
                ["lastTransitionAtUtc"] = now.ToString("O")
            };
            stateArr.Add(st);
            // Emit an initial "enter" only when starting inside.
            if (inside)
            {
                events.Add(BuildGeofenceEventJson("geofence_enter", now, spec, lat, lon, dist));
            }
            continue;
        }

        var prevInside = st["inside"] is System.Text.Json.Nodes.JsonValue piv && piv.TryGetValue<bool>(out var pb) ? pb : false;
        if (prevInside != inside)
        {
            st["inside"] = inside;
            st["lastTransitionAtUtc"] = now.ToString("O");
            events.Add(BuildGeofenceEventJson(inside ? "geofence_enter" : "geofence_exit", now, spec, lat, lon, dist));
        }
    }

    state["lastEvaluatedAtUtc"] = now.ToString("O");

    updatedProfileJson = root.ToJsonString(new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = false });
    geofenceEventsJsonArray = events.ToJsonString(new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = false });
    return true;
}

static System.Text.Json.Nodes.JsonObject BuildGeofenceEventJson(string kind, DateTimeOffset nowUtc, (string Id, string? Name, double Lat, double Lon, double RadiusM) spec, double curLat, double curLon, double distanceM)
{
    var details = new System.Text.Json.Nodes.JsonObject
    {
        ["geofenceId"] = spec.Id,
        ["name"] = spec.Name ?? spec.Id,
        ["center"] = new System.Text.Json.Nodes.JsonObject { ["latitude"] = spec.Lat, ["longitude"] = spec.Lon },
        ["radiusMeters"] = spec.RadiusM,
        ["distanceMeters"] = Math.Round(distanceM, 1),
        ["at"] = new System.Text.Json.Nodes.JsonObject { ["latitude"] = curLat, ["longitude"] = curLon },
    };

    return new System.Text.Json.Nodes.JsonObject
    {
        ["eventId"] = Guid.NewGuid(),
        ["occurredAtUtc"] = nowUtc.ToString("O"),
        ["kind"] = kind,
        ["app"] = null,
        ["details"] = details.ToJsonString(new JsonSerializerOptions(JsonDefaults.Options) { WriteIndented = false }),
        ["deviceId"] = null,
    };
}

static bool TryParseGeofenceJsonElement(JsonElement obj, out (string Id, string? Name, double Lat, double Lon, double RadiusM) spec)
{
    spec = default;
    if (obj.ValueKind != JsonValueKind.Object) return false;
    string? id = null;
    string? name = null;
    double lat = 0, lon = 0, radius = 0;
    if (obj.TryGetProperty("id", out var idp) && idp.ValueKind == JsonValueKind.String) id = idp.GetString();
    if (obj.TryGetProperty("name", out var np) && np.ValueKind == JsonValueKind.String) name = np.GetString();
    if (obj.TryGetProperty("latitude", out var lap) && lap.ValueKind == JsonValueKind.Number) lap.TryGetDouble(out lat);
    if (obj.TryGetProperty("longitude", out var lop) && lop.ValueKind == JsonValueKind.Number) lop.TryGetDouble(out lon);
    if (obj.TryGetProperty("radiusMeters", out var rp) && rp.ValueKind == JsonValueKind.Number) rp.TryGetDouble(out radius);
    else if (obj.TryGetProperty("radiusM", out var rp2) && rp2.ValueKind == JsonValueKind.Number) rp2.TryGetDouble(out radius);

    if (string.IsNullOrWhiteSpace(id) || radius <= 0) return false;
    if (double.IsNaN(lat) || double.IsNaN(lon)) return false;
    spec = (id.Trim(), string.IsNullOrWhiteSpace(name) ? null : name.Trim(), lat, lon, radius);
    return true;
}

static bool TryParseGeofenceString(string? line, out (string Id, string? Name, double Lat, double Lon, double RadiusM) spec)
{
    spec = default;
    if (string.IsNullOrWhiteSpace(line)) return false;
    var s = line.Trim();
    // Allow JSON per-line.
    if (s.StartsWith("{") && s.EndsWith("}"))
    {
        try
        {
            using var doc = JsonDocument.Parse(s);
            return TryParseGeofenceJsonElement(doc.RootElement, out spec);
        }
        catch { return false; }
    }

    // CSV: id,name?,lat,lon,radiusM OR id,lat,lon,radiusM
    var parts = s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length == 4)
    {
        var id = parts[0];
        if (!double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)) return false;
        if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon)) return false;
        if (!double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rad)) return false;
        if (string.IsNullOrWhiteSpace(id) || rad <= 0) return false;
        spec = (id.Trim(), null, lat, lon, rad);
        return true;
    }
    if (parts.Length == 5)
    {
        var id = parts[0];
        var name = parts[1];
        if (!double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)) return false;
        if (!double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon)) return false;
        if (!double.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rad)) return false;
        if (string.IsNullOrWhiteSpace(id) || rad <= 0) return false;
        spec = (id.Trim(), string.IsNullOrWhiteSpace(name) ? null : name.Trim(), lat, lon, rad);
        return true;
    }

    return false;
}

static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
{
    const double R = 6371000.0; // meters
    static double ToRad(double deg) => deg * (Math.PI / 180.0);
    var dLat = ToRad(lat2 - lat1);
    var dLon = ToRad(lon2 - lon1);
    var a = Math.Pow(Math.Sin(dLat / 2), 2) + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2);
    var c = 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    return R * c;
}
// Serve the local dashboard (static; SPA-style routing via index.html fallback).
app.UseDefaultFiles();
// Static assets are loaded by an embedded WebView2 in the Windows Parent app.
// Avoid aggressive caching: patch-based updates can change JS/CSS content while
// keeping stable URLs. WebView caches can otherwise serve stale/broken scripts.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        if (path.StartsWith("/app/", StringComparison.OrdinalIgnoreCase))
        {
            // Always revalidate. (No-store is the safest for rapid dev/patching.)
            ctx.Context.Response.Headers["Cache-Control"] = "no-store";
            ctx.Context.Response.Headers["Pragma"] = "no-cache";
            ctx.Context.Response.Headers["Expires"] = "0";
        }
    }
});
app.MapFallbackToFile("index.html");

// Bind explicitly to loopback only.
app.Urls.Clear();
app.Urls.Add("http://127.0.0.1:8765");

app.Run();
