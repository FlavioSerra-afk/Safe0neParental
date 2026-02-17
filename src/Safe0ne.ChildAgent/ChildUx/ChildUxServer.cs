using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using Microsoft.Extensions.Logging;
using Safe0ne.Shared.Contracts;
using Safe0ne.ChildAgent.Requests;

namespace Safe0ne.ChildAgent.ChildUx;

/// <summary>
/// K7: Local child-facing UX served on loopback.
/// Uses TcpListener (not HttpListener) to avoid URL ACL / admin requirements.
/// </summary>
public sealed class ChildUxServer
{
    private readonly object _lock = new();
    private readonly ChildStateStore _store;
    private readonly ILogger _logger;
    private readonly AccessRequestQueue _requests;
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;

    // Keep this separate from the Control Plane (8765) and web block server (80/8766).
    private const int Port = 8771;

    public ChildUxServer(ChildStateStore store, AccessRequestQueue requests, ILogger<ChildUxServer> logger)
    {
        _store = store;
        _requests = requests;
        _logger = logger;
    }

    public string BaseUrl => $"http://127.0.0.1:{Port}/";

    public void Start()
    {
        lock (_lock)
        {
            if (_listener is not null) return;

            try
            {
                _cts = new CancellationTokenSource();
                _listener = new TcpListener(IPAddress.Loopback, Port);
                _listener.Start();
                _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
                _logger.LogInformation("Child UX: {Url}today", BaseUrl);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Child UX server failed to start");
                _listener = null;
                _cts?.Dispose();
                _cts = null;
            }
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { _cts?.Cancel(); } catch { }
            try { _listener?.Stop(); } catch { }
            _listener = null;
            try { _cts?.Dispose(); } catch { }
            _cts = null;
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient? client = null;
            try
            {
                var l = _listener;
                if (l is null) break;
                client = await l.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct));
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                try { client?.Dispose(); } catch { }
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        using (client)
        {
            try
            {
                using var stream = client.GetStream();
                stream.ReadTimeout = 3000;
                stream.WriteTimeout = 3000;

                // Minimal HTTP request parsing (first line only).
                var requestLine = await ReadLineAsync(stream, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(requestLine)) return;

                var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var rawPath = parts.Length >= 2 ? parts[1] : "/";

                // Drain headers quickly (ignore values).
                while (true)
                {
                    var line = await ReadLineAsync(stream, ct).ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line)) break;
                }

                var (path, query) = SplitPathAndQuery(rawPath);
                var html = path switch
                {
                    "/" or "/today" => RenderToday(query),
                    "/request" => HandleRequest(query),
                    "/blocked" => RenderBlocked(query),
                    "/pair" => RenderPair(query),
                    "/pair/complete" => HandlePair(query),
                    _ => RenderNotFound()
                };

                var body = Encoding.UTF8.GetBytes(html);
                var header = $"HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
                var headerBytes = Encoding.ASCII.GetBytes(header);

                await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
                await stream.WriteAsync(body, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (!ct.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buf, 0, 1, ct).ConfigureAwait(false);
            }
            catch
            {
                return null;
            }

            if (read <= 0) return sb.Length == 0 ? null : sb.ToString();
            var ch = (char)buf[0];
            if (ch == '\n') break;
            if (ch != '\r') sb.Append(ch);
            if (sb.Length > 8192) break;
        }
        return sb.ToString();
    }

    private static (string Path, string Query) SplitPathAndQuery(string raw)
    {
        var idx = raw.IndexOf('?');
        return idx >= 0 ? (raw[..idx], raw[(idx + 1)..]) : (raw, string.Empty);
    }

    private string RenderToday(string query)
    {
        var snap = _store.GetSnapshot();
        var eff = snap.Effective;
        var pol = snap.Policy;
        var st = snap.ScreenTime;

        var now = DateTimeOffset.Now;
        var nextChange = ScheduleHelper.GetNextChange(now, pol);

        var limitLabel = st.LimitEnabled ? FormatTime(st.RemainingToday) : "No daily limit";
        var usedLabel = st.LimitEnabled ? FormatTime(st.UsedToday) : FormatTime(st.UsedToday);

        var mode = eff?.EffectiveMode.ToString() ?? pol?.Mode.ToString() ?? "Unknown";
        var activeSchedule = eff?.ActiveSchedule ?? "None";

        var allowedApps = (pol?.AllowedProcessNames ?? Array.Empty<string>()).Take(12).ToArray();
        var allowedSites = (pol?.WebAllowedDomains ?? Array.Empty<string>()).Take(12).ToArray();

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'/><title>Safe0ne Today</title>");
        sb.Append("<style>body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial;margin:24px;} .grid{max-width:860px;display:grid;gap:12px;} .card{border:1px solid #ddd;border-radius:12px;padding:16px;} .muted{color:#666;} ul{margin:8px 0 0 20px;} a{color:#0b57d0;text-decoration:none;} a:hover{text-decoration:underline;}</style>");
        sb.Append("</head><body><div class='grid'>");
        sb.Append("<div class='card'><h1 style='margin:0'>Today</h1><div class='muted'>This is a local page on this device.</div></div>");

        sb.Append("<div class='card'>");
        sb.Append($"<h2 style='margin:0 0 8px 0'>Screen time</h2><div><b>Remaining:</b> {WebUtility.HtmlEncode(limitLabel)}</div><div class='muted'><b>Used:</b> {WebUtility.HtmlEncode(usedLabel)}</div>");
        sb.Append("</div>");

        sb.Append("<div class='card'>");
        sb.Append($"<h2 style='margin:0 0 8px 0'>Status</h2><div><b>Mode:</b> {WebUtility.HtmlEncode(mode)}</div><div><b>Active schedule:</b> {WebUtility.HtmlEncode(activeSchedule)}</div>");
        if (nextChange is not null)
        {
            sb.Append($"<div class='muted'><b>Next change:</b> {WebUtility.HtmlEncode(nextChange.Name)} at {WebUtility.HtmlEncode(nextChange.WhenLocal.ToString("t"))}</div>");
        }
        sb.Append("</div>");

        // Active grants (K8/P11): show current time-boxed exceptions.
        var grants = eff?.ActiveGrants ?? Array.Empty<Grant>();
        if (grants.Length > 0)
        {
            sb.Append("<div class='card'>");
            sb.Append("<h2 style='margin:0 0 8px 0'>Active grants</h2>");
            sb.Append("<div class='muted'>These temporary allowances expire automatically.</div>");
            sb.Append("<ul>");
            foreach (var g in grants.OrderBy(x => x.ExpiresAtUtc).Take(10))
            {
                var until = g.ExpiresAtUtc.ToLocalTime().ToString("t");
                var label = g.Type switch
                {
                    GrantType.ExtraScreenTime => g.ExtraMinutes is null ? "Extra screen time" : $"+{g.ExtraMinutes} minutes",
                    GrantType.UnblockApp => "Unblock app",
                    GrantType.UnblockSite => "Unblock site",
                    _ => "Grant"
                };
                var target = string.IsNullOrWhiteSpace(g.Target) ? "" : $": {g.Target}";
                sb.Append($"<li>{WebUtility.HtmlEncode(label)}{WebUtility.HtmlEncode(target)} <span class='muted'>(until {WebUtility.HtmlEncode(until)})</span></li>");
            }
            sb.Append("</ul>");
            sb.Append("</div>");
        }

        sb.Append("<div class='card'>");
        sb.Append("<h2 style='margin:0 0 8px 0'>Always allowed</h2>");
        sb.Append("<div class='muted'>Some apps and sites can be marked as always allowed by your parent.</div>");
        sb.Append("<div style='display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:8px'>");
        sb.Append("<div><b>Apps</b>");
        sb.Append(RenderListOrEmpty(allowedApps, "(none configured)"));
        sb.Append("</div>");
        sb.Append("<div><b>Websites</b>");
        sb.Append(RenderListOrEmpty(allowedSites, "(none configured)"));
        sb.Append("</div>");
        sb.Append("</div>");
        sb.Append("</div>");

        sb.Append("<div class='card'>");
        sb.Append("<h2 style='margin:0 0 8px 0'>Need help?</h2>");
        sb.Append("<div class='muted'>If something is blocked incorrectly, you can ask a parent to review it. You can also send a request from this page.</div>");
        // Quick requests. Keep minimal and safe: use GET forms to local endpoints.
        if (st.LimitEnabled)
        {
            sb.Append("<div style='margin-top:10px'><a href='/request?type=more_time&amp;minutes=15&amp;target=screen_time'>Request +15 minutes</a></div>");
        }
        else
        {
            sb.Append("<div class='muted' style='margin-top:10px'>More time requests are available when a daily limit is enabled.</div>");
        }

        sb.Append("<div style='display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-top:12px'>");
        sb.Append("<form method='get' action='/request'><input type='hidden' name='type' value='unblock_app'/>" +
                  "<div class='muted' style='margin-bottom:6px'>Request unblock app</div>" +
                  "<input name='target' placeholder='eg. chrome.exe' style='width:100%;padding:8px;border:1px solid #ddd;border-radius:8px'/>" +
                  "<div style='margin-top:8px'><button type='submit' style='padding:8px 12px;border:1px solid #ddd;border-radius:10px;background:#fff;cursor:pointer'>Send</button></div></form>");

        sb.Append("<form method='get' action='/request'><input type='hidden' name='type' value='unblock_site'/>" +
                  "<div class='muted' style='margin-bottom:6px'>Request unblock site</div>" +
                  "<input name='target' placeholder='eg. example.com' style='width:100%;padding:8px;border:1px solid #ddd;border-radius:8px'/>" +
                  "<div style='margin-top:8px'><button type='submit' style='padding:8px 12px;border:1px solid #ddd;border-radius:10px;background:#fff;cursor:pointer'>Send</button></div></form>");
        sb.Append("</div>");
        sb.Append(RenderRecentRequestsHtml(eff?.ActiveGrants));
        sb.Append("</div>");

        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private string RenderBlocked(string query)
    {
        // Query string fields are optional.
        var q = ParseQuery(query);
        q.TryGetValue("kind", out var kind);
        q.TryGetValue("target", out var target);
        q.TryGetValue("reason", out var reason);

        // Back-compat: older links/screens may use `reason=` instead of `kind=`.
        if (string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(reason))
        {
            kind = reason;
        }

        var snap = _store.GetSnapshot();
        var eff = snap.Effective;
        var pol = snap.Policy;

        var mode = eff?.EffectiveMode.ToString() ?? pol?.Mode.ToString() ?? "Unknown";
        var activeSchedule = eff?.ActiveSchedule ?? "None";

        var title = kind switch
        {
            "web" => "Website blocked",
            "app" => "App blocked",
            "time" => "Time limit reached",
            _ => "Blocked"
        };

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset='utf-8'/><title>Safe0ne Blocked</title>");
        sb.Append("<style>body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial;margin:24px;} .card{max-width:860px;border:1px solid #ddd;border-radius:12px;padding:16px;} .muted{color:#666;} a{color:#0b57d0;text-decoration:none;} a:hover{text-decoration:underline;}</style>");
        sb.Append("</head><body><div class='card'>");
        sb.Append($"<h1 style='margin:0 0 8px 0'>{WebUtility.HtmlEncode(title)}</h1>");

        if (!string.IsNullOrWhiteSpace(target))
            sb.Append($"<div><b>Target:</b> {WebUtility.HtmlEncode(target)}</div>");
        if (!string.IsNullOrWhiteSpace(reason))
            sb.Append($"<div class='muted'><b>Reason:</b> {WebUtility.HtmlEncode(reason)}</div>");

        sb.Append($"<div style='margin-top:12px'><b>Mode:</b> {WebUtility.HtmlEncode(mode)} · <b>Active schedule:</b> {WebUtility.HtmlEncode(activeSchedule)}</div>");
        sb.Append("<div class='muted' style='margin-top:8px'>If this seems wrong, ask a parent to review your rules. You can also send a request from this screen.</div>");

        // Request actions (minimal v1)
        if (string.Equals(kind, "time", StringComparison.OrdinalIgnoreCase))
        {
            sb.Append("<div style='margin-top:12px'><a href='/request?type=more_time&amp;minutes=15&amp;target=screen_time'>Request +15 minutes</a></div>");
        }
        else if (string.Equals(kind, "app", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(target))
        {
            sb.Append($"<div style='margin-top:12px'><a href='/request?type=unblock_app&amp;target={WebUtility.UrlEncode(target)}&amp;reason=blocked'>Request unblock app</a></div>");
        }
        else if (string.Equals(kind, "app", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(target))
        {
            sb.Append("<form method='get' action='/request' style='margin-top:12px'>" +
                      "<input type='hidden' name='type' value='unblock_app'/>" +
                      "<div class='muted' style='margin-bottom:6px'>Which app is blocked?</div>" +
                      "<input name='target' placeholder='eg. chrome.exe' style='width:100%;max-width:420px;padding:8px;border:1px solid #ddd;border-radius:8px'/>" +
                      "<div style='margin-top:8px'><button type='submit' style='padding:8px 12px;border:1px solid #ddd;border-radius:10px;background:#fff;cursor:pointer'>Request unblock app</button></div>" +
                      "</form>");
        }
        else if (string.Equals(kind, "web", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(target))
        {
            sb.Append($"<div style='margin-top:12px'><a href='/request?type=unblock_site&amp;target={WebUtility.UrlEncode(target)}&amp;reason=blocked'>Request unblock site</a></div>");
        }
        else if (string.Equals(kind, "web", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(target))
        {
            sb.Append("<form method='get' action='/request' style='margin-top:12px'>" +
                      "<input type='hidden' name='type' value='unblock_site'/>" +
                      "<div class='muted' style='margin-bottom:6px'>Which site is blocked?</div>" +
                      "<input name='target' placeholder='eg. example.com' style='width:100%;max-width:420px;padding:8px;border:1px solid #ddd;border-radius:8px'/>" +
                      "<div style='margin-top:8px'><button type='submit' style='padding:8px 12px;border:1px solid #ddd;border-radius:10px;background:#fff;cursor:pointer'>Request unblock site</button></div>" +
                      "</form>");
        }

        sb.Append(RenderRecentRequestsHtml(eff?.ActiveGrants));

        sb.Append($"<div style='margin-top:12px'><a href='{WebUtility.HtmlEncode(BaseUrl)}today'>View Today</a></div>");
        sb.Append("</div></body></html>");
        return sb.ToString();
    }

    private string HandleRequest(string query)
    {
        var q = ParseQuery(query);
        q.TryGetValue("type", out var typeRaw);
        q.TryGetValue("target", out var targetRaw);
        q.TryGetValue("reason", out var reason);
        q.TryGetValue("minutes", out var minutesRaw);

        var childId = ResolveChildId();

        AccessRequestType type;
        string target;

        if (string.Equals(typeRaw, "more_time", StringComparison.OrdinalIgnoreCase))
        {
            type = AccessRequestType.MoreTime;
            target = string.IsNullOrWhiteSpace(targetRaw) ? "screen_time" : targetRaw;
            if (int.TryParse(minutesRaw, out var mins) && mins > 0)
            {
                reason = string.IsNullOrWhiteSpace(reason) ? $"Request +{mins} minutes" : reason;
            }
        }
        else if (string.Equals(typeRaw, "unblock_app", StringComparison.OrdinalIgnoreCase))
        {
            type = AccessRequestType.UnblockApp;
            target = string.IsNullOrWhiteSpace(targetRaw) ? "" : targetRaw;
        }
        else if (string.Equals(typeRaw, "unblock_site", StringComparison.OrdinalIgnoreCase))
        {
            type = AccessRequestType.UnblockSite;
            target = string.IsNullOrWhiteSpace(targetRaw) ? "" : targetRaw;
        }
        else
        {
            return RenderSimpleMessage("Request", "Unknown request type.", "/today", "Back to Today");
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            return RenderSimpleMessage("Request", "Missing target.", "/today", "Back to Today");
        }

        var result = _requests.Enqueue(childId, type, target, reason);
        var msg = result.Outcome == AccessRequestQueue.EnqueueOutcome.Deduped
            ? "You already sent a similar request recently."
            : "Request queued and will be sent when the service is available.";

        return RenderSimpleMessage("Request sent", msg, "/today", "Back to Today");
    }

    private string RenderRecentRequestsHtml(Grant[]? activeGrants)
    {
        try
        {
            var childId = ResolveChildId();
            var items = _requests.GetRecent(childId, max: 5);
            var telem = _requests.GetTelemetry(childId);
            if (items.Count == 0) return "";

            var grants = activeGrants ?? Array.Empty<Grant>();

            var sb = new StringBuilder();
            sb.Append("<div style='margin-top:12px'><b>Recent requests</b>");
            if (telem.HasPendingOutbox)
            {
                TimeSpan? next = telem.NextAttemptUtc is null ? null : (TimeSpan?)(telem.NextAttemptUtc.Value - DateTimeOffset.UtcNow);
                var nextLabel = next is null ? "" : $" Next retry in {Math.Max(0, (int)next.Value.TotalSeconds)}s.";
                var err = string.IsNullOrWhiteSpace(telem.LastError) ? "" : $" Last error: {WebUtility.HtmlEncode(telem.LastError)}.";
                sb.Append($"<div class='muted'>Queued requests pending delivery.{err}{nextLabel}</div>");
            }
            sb.Append("<ul>");
            foreach (var i in items)
            {
                var label = i.Type switch
                {
                    AccessRequestType.MoreTime => "More time",
                    AccessRequestType.UnblockApp => "Unblock app",
                    AccessRequestType.UnblockSite => "Unblock site",
                    _ => "Request"
                };

                var status = i.Status switch
                {
                    AccessRequestStatus.Pending => "Pending",
                    AccessRequestStatus.Approved => "Approved",
                    AccessRequestStatus.Denied => "Denied",
                    AccessRequestStatus.Cancelled => "Cancelled",
                    _ => i.Status.ToString()
                };

                if (i.Status == AccessRequestStatus.Pending && _requests.IsQueued(childId, i.RequestId))
                {
                    status = "Queued";
                }

                string? extra = null;
                if (i.Status == AccessRequestStatus.Approved)
                {
                    // Best-effort: correlate to a grant for "approved until" UX.
                    var g = grants.FirstOrDefault(x => x.SourceRequestId.HasValue && x.SourceRequestId.Value == i.RequestId);
                    if (g is null)
                    {
                        // Fallback match by type/target for older data without SourceRequestId
                        g = grants.FirstOrDefault(x =>
                            (i.Type == AccessRequestType.UnblockApp && x.Type == GrantType.UnblockApp && string.Equals(x.Target, i.Target, StringComparison.OrdinalIgnoreCase)) ||
                            (i.Type == AccessRequestType.UnblockSite && x.Type == GrantType.UnblockSite && string.Equals(x.Target, i.Target, StringComparison.OrdinalIgnoreCase)) ||
                            (i.Type == AccessRequestType.MoreTime && x.Type == GrantType.ExtraScreenTime));
                    }

                    if (g is not null)
                    {
                        var untilLocal = g.ExpiresAtUtc.ToLocalTime().ToString("t");
                        if (g.Type == GrantType.ExtraScreenTime && g.ExtraMinutes is not null)
                        {
                            extra = $"+{g.ExtraMinutes}m (until {untilLocal})";
                        }
                        else
                        {
                            extra = $"until {untilLocal}";
                        }
                    }
                }

                var statusHtml = extra is null
                    ? $"<b>{WebUtility.HtmlEncode(status)}</b>"
                    : $"<b>{WebUtility.HtmlEncode(status)}</b> <span class='muted'>({WebUtility.HtmlEncode(extra)})</span>";

                sb.Append($"<li>{WebUtility.HtmlEncode(label)}: {WebUtility.HtmlEncode(i.Target)} — {statusHtml}</li>");
            }
            sb.Append("</ul></div>");
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static string RenderSimpleMessage(string title, string message, string linkHref, string linkText)
    {
        return $"<!doctype html><html><head><meta charset='utf-8'/><title>{WebUtility.HtmlEncode(title)}</title>" +
               "<style>body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial;margin:24px;} .card{max-width:860px;border:1px solid #ddd;border-radius:12px;padding:16px;} .muted{color:#666;} a{color:#0b57d0;text-decoration:none;} a:hover{text-decoration:underline;}</style>" +
               "</head><body><div class='card'>" +
               $"<h1 style='margin:0 0 8px 0'>{WebUtility.HtmlEncode(title)}</h1>" +
               $"<div class='muted'>{WebUtility.HtmlEncode(message)}</div>" +
               $"<div style='margin-top:12px'><a href='{WebUtility.HtmlEncode(linkHref)}'>{WebUtility.HtmlEncode(linkText)}</a></div>" +
               "</div></body></html>";
    }

    private static readonly Guid DefaultChildGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ChildId ResolveChildId()
    {
        var raw = Environment.GetEnvironmentVariable("SAFEONE_CHILD_ID");
        if (!string.IsNullOrWhiteSpace(raw) && Guid.TryParse(raw, out var g))
        {
            return new ChildId(g);
        }
        return new ChildId(DefaultChildGuid);
    }

    private static string RenderNotFound()
        => "<!doctype html><html><head><meta charset='utf-8'/><title>Not Found</title></head><body>Not Found</body></html>";

    private static string FormatTime(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
        {
            var h = (int)ts.TotalHours;
            var m = ts.Minutes;
            return $"{h}h {m}m";
        }
        if (ts.TotalMinutes >= 1)
        {
            var m = (int)ts.TotalMinutes;
            var s = ts.Seconds;
            return $"{m}m {s}s";
        }
        return $"{Math.Max(0, (int)ts.TotalSeconds)}s";
    }

    private static string RenderListOrEmpty(string[] items, string empty)
    {
        if (items.Length == 0) return $"<div class='muted'>{WebUtility.HtmlEncode(empty)}</div>";
        var sb = new StringBuilder("<ul>");
        foreach (var i in items)
        {
            sb.Append($"<li>{WebUtility.HtmlEncode(i)}</li>");
        }
        sb.Append("</ul>");
        return sb.ToString();
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query)) return dict;
        var parts = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var idx = p.IndexOf('=');
            if (idx <= 0) continue;
            var k = Uri.UnescapeDataString(p[..idx]);
            var v = Uri.UnescapeDataString(p[(idx + 1)..]);
            dict[k] = v;
        }
        return dict;
    }


private static string NormalizePairingCode(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
    var digits = new string(raw.Where(char.IsDigit).ToArray());
    return digits;
}

private string RenderPair(string query)
{
    var sb = new StringBuilder();
    sb.Append("<!doctype html><html><head><meta charset='utf-8'/><title>Pair device</title>");
    sb.Append("<style>body{font-family:system-ui,-apple-system,Segoe UI,Roboto,Arial;margin:24px;} .card{max-width:720px;border:1px solid #ddd;border-radius:14px;padding:16px;} .muted{color:#666;} input{width:100%;max-width:520px;padding:10px;border:1px solid #ddd;border-radius:10px;} button{padding:10px 14px;border:1px solid #ddd;border-radius:12px;background:#fff;cursor:pointer;} a{color:#0b57d0;text-decoration:none;} a:hover{text-decoration:underline;} .ok{background:#ecfdf5;border:1px solid #10b98133;border-radius:12px;padding:10px 12px;margin-bottom:12px;} .warn{background:#fffbeb;border:1px solid #f59e0b33;border-radius:12px;padding:10px 12px;margin-bottom:12px;}</style>");
    sb.Append("</head><body><div class='card'>");
    sb.Append("<h1 style='margin:0 0 8px 0'>Pair this device</h1>");
    sb.Append("<div class='muted'>Enter the pairing code shown in the Parent app. This page runs locally on this device (loopback).</div>");
    sb.Append("<div class='muted' style='margin-top:10px'>Codes are 6 digits. You can paste with or without spaces/dashes.</div>");

    var current = AgentAuthStore.LoadCurrentChildId();
    if (current.HasValue)
    {
        sb.Append($"<div class='ok'><strong>Already paired</strong><div class='muted'>ChildId: {WebUtility.HtmlEncode(current.Value.Value.ToString())}</div></div>");
    }
    else
    {
        sb.Append("<div class='warn'><strong>Not paired yet</strong><div class='muted'>After you submit the code, the agent will attempt pairing on its next heartbeat.</div></div>");
    }

    sb.Append("<form method='get' action='/pair/complete' style='margin-top:14px'>");
    sb.Append("<div class='muted' style='margin-bottom:6px'>Pairing code</div>");
    sb.Append("<input name='code' placeholder='eg. 123456' autocomplete='one-time-code' maxlength='16'/>");
    sb.Append("<div class='muted' style='margin:12px 0 6px 0'>Device name (optional)</div>");
    sb.Append($"<input name='name' value='{WebUtility.HtmlEncode(Environment.MachineName)}'/>");
    sb.Append("<div style='margin-top:14px;display:flex;gap:10px;flex-wrap:wrap;'>");
    sb.Append("<button type='submit'>Save code</button>");
    sb.Append($"<a href='{WebUtility.HtmlEncode(BaseUrl)}today' style='align-self:center'>Back to Today</a>");
    sb.Append("</div>");
    sb.Append("</form>");
    sb.Append("</div></body></html>");
    return sb.ToString();
}

private string HandlePair(string query)
{
    var q = ParseQuery(query);
    q.TryGetValue("code", out var raw);
    q.TryGetValue("name", out var name);

    var code = NormalizePairingCode(raw);

    if (string.IsNullOrWhiteSpace(code) || code.Length < 6)
    {
        return RenderSimpleMessage("Pair device", "Pairing code looks invalid. Please enter the 6-digit code from the Parent app.", "/pair", "Try again");
    }

    // Store code in-process so HeartbeatWorker can use it (SAFEONE_PAIR_CODE).
    // NOTE: ChildId is still resolved at agent startup. If the agent was launched for the wrong ChildId, restart after fixing config.
    Environment.SetEnvironmentVariable("SAFEONE_PAIR_CODE", code);

    if (!string.IsNullOrWhiteSpace(name))
    {
        Environment.SetEnvironmentVariable("SAFEONE_DEVICE_NAME", name.Trim());
    }

    return RenderSimpleMessage("Pairing queued", "Pairing code saved. The agent will attempt pairing on its next heartbeat. Return to Today and wait for status to update.", "/today", "Go to Today");
}

}