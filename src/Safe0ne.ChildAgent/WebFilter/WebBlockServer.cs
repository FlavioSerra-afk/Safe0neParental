
using System.Net;
using System.Text;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.WebFilter;

internal sealed class WebBlockServer
{
    private readonly object _lock = new();
    private HttpListener? _listener;
    private readonly Dictionary<string, int> _hits = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _domainReasons = new(StringComparer.OrdinalIgnoreCase);

    // Prefer port 80 so hosts-file redirection (domain -> 127.0.0.1) reaches us.
    // Fallback to 8766 if URL ACL / elevation prevents port 80.
    private const int PreferredPort = 80;
    private const int FallbackPort = 8766;
    private int _listeningPort = FallbackPort;

    public void EnsureStarted()
    {
        lock (_lock)
        {
            if (_listener is not null) return;

            try
            {
                _listener = new HttpListener();

                if (!TryStartOnPort(_listener, PreferredPort))
                {
                    TryStartOnPort(_listener, FallbackPort);
                }

                if (!_listener.IsListening)
                {
                    _listener = null;
                    return;
                }

                _ = Task.Run(async () =>
                {
                    while (_listener is not null && _listener.IsListening)
                    {
                        HttpListenerContext ctx;
                        try
                        {
                            ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                        }
                        catch
                        {
                            break;
                        }

                        var host = ctx.Request.Headers["Host"] ?? "blocked.site";
                        var domain = DomainCategoryEngine.NormalizeDomain(host);
                        if (domain.Length == 0) domain = "blocked.site";

                        lock (_lock)
                        {
                            _hits.TryGetValue(domain, out var c);
                            _hits[domain] = c + 1;
                        }

                        var html = BuildHtml(domain, _listeningPort);
                        var bytes = Encoding.UTF8.GetBytes(html);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.ContentLength64 = bytes.Length;
                        await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                        ctx.Response.Close();
                    }
                });
            }
            catch
            {
                // Best effort. If HttpListener fails (ACL), we still enforce via hosts.
                _listener = null;
            }
        }
    }

    private bool TryStartOnPort(HttpListener l, int port)
    {
        try
        {
            var prefix = $"http://127.0.0.1:{port}/";
            l.Prefixes.Clear();
            l.Prefixes.Add(prefix);
            l.Start();
            _listeningPort = port;
            return true;
        }
        catch
        {
            try { l.Stop(); } catch { }
            return false;
        }
    }

    public WebBlockedDomainItem[]? TakeTopBlockedDomains(int maxItems)
    {
        lock (_lock)
        {
            if (_hits.Count == 0) return Array.Empty<WebBlockedDomainItem>();
            var items = _hits.OrderByDescending(kv => kv.Value)
                .Take(maxItems)
                .Select(kv => new WebBlockedDomainItem(kv.Key, kv.Value, ReasonFor(kv.Key)))
                .ToArray();
            return items;
        }
    }

    /// <summary>
    /// Returns the top blocked domains since the last call and clears the counter.
    /// This prevents unbounded growth and provides a natural per-heartbeat delta.
    /// </summary>
    public WebBlockedDomainItem[] TakeTopBlockedDomainsAndReset(int maxItems)
    {
        lock (_lock)
        {
            if (_hits.Count == 0) return Array.Empty<WebBlockedDomainItem>();
            var items = _hits.OrderByDescending(kv => kv.Value)
                .Take(maxItems)
                .Select(kv => new WebBlockedDomainItem(kv.Key, kv.Value, ReasonFor(kv.Key)))
                .ToArray();

            _hits.Clear();
            return items;
        }
    }

    /// <summary>
    /// Best-effort: attach a reason to each domain, so the parent UI can distinguish
    /// category "Alert" (allow-but-alert in future) from strict blocks.
    /// </summary>
    public void SetDomainReasons(Dictionary<string, string> reasons)
    {
        lock (_lock)
        {
            _domainReasons = reasons ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private string ReasonFor(string domain)
    {
        if (_domainReasons.TryGetValue(domain, out var r) && !string.IsNullOrWhiteSpace(r))
            return r;
        return "hosts_redirect";
    }

    private static string BuildHtml(string domain, int port)
    {
        var childUxBlocked = $"http://127.0.0.1:8771/blocked?kind=web&target={WebUtility.UrlEncode(domain)}&reason=web_filter";
        var childUxToday = "http://127.0.0.1:8771/today";
        return $@"<!doctype html>
<html>
<head><meta charset='utf-8'/><title>Blocked</title>
<style>
body{{font-family: system-ui, -apple-system, Segoe UI, Roboto, Arial; margin:40px;}}
.card{{max-width:720px; padding:24px; border:1px solid #ddd; border-radius:12px;}}
.hint{{color:#666; margin-top:8px;}}
</style>
</head>
<body>
  <div class='card'>
    <h1>Site blocked</h1>
    <div><b>{WebUtility.HtmlEncode(domain)}</b></div>
    <div class='hint'>This site was blocked by your family rules (Windows-first prototype). If this seems wrong, ask a parent to review the policy.</div>
    <div class='hint' style='margin-top:12px'>
        <a href='{WebUtility.HtmlEncode(childUxBlocked)}'>Why was this blocked?</a> ·
        <a href='{WebUtility.HtmlEncode(childUxToday)}'>View Today</a>
    </div>
  </div>
</body>
</html>";
    }
}
