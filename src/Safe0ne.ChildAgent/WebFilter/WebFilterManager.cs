
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.WebFilter;

/// <summary>
/// Windows-first web filtering (best effort).
/// - Applies explicit blocked domains (and a small built-in category list) via hosts-file entries to 127.0.0.1.
/// - Runs a tiny local HTTP server to display an explainable block page for HTTP requests.
/// - Emits privacy-first aggregates + circumvention detection signals in heartbeats.
/// </summary>
public sealed class WebFilterManager
{
    private readonly ChildId _childId;
    private readonly HostsFileManager _hosts;
    private readonly WebBlockServer _blockServer;
    private readonly DomainCategoryEngine _categoryEngine;
    private readonly CircumventionDetector _circDetector;

    private string? _lastAppliedHash;

    public WebFilterManager(ChildId childId)
    {
        _childId = childId;
        _hosts = new HostsFileManager();
        _blockServer = new WebBlockServer();
        _categoryEngine = new DomainCategoryEngine();
        _circDetector = new CircumventionDetector();
    }

    public (WebReport Web, CircumventionSignals Circumvention) TickAndApply(DateTimeOffset nowUtc, ChildPolicy? policy, bool isActive)
    {
        // Always run the block server (safe no-op if already started)
        _blockServer.EnsureStarted();

        if (policy is null)
        {
            return (new WebReport(LocalDateString(nowUtc.ToLocalTime())), _circDetector.Detect(false, null));
        }

        var allow = NormalizeDomains(policy.WebAllowedDomains);
        var block = NormalizeDomains(policy.WebBlockedDomains);

        // Category rules (prototype): convert categories set to Block/Alert into domain sets.
        // NOTE: In the current Windows-first hosts-based implementation, "Alert" is tracked separately
        // but still results in a block page (allow-but-alert requires a deeper network hook).
        var rules = policy.WebCategoryRules ?? Array.Empty<WebCategoryRule>();
        var blockedCategories = rules.Where(r => r.Action == WebRuleAction.Block).Select(r => r.Category).ToHashSet();
        var alertCategories = rules.Where(r => r.Action == WebRuleAction.Alert).Select(r => r.Category).ToHashSet();

        var categoryBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var categoryAlert = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var domain in _categoryEngine.GetKnownDomains())
        {
            var cat = _categoryEngine.Classify(domain);
            if (cat == WebCategory.Adult && policy.WebAdultBlockEnabled) // adult toggle also blocks adult list
            {
                categoryBlocked.Add(domain);
                continue;
            }
            if (blockedCategories.Contains(cat))
            {
                categoryBlocked.Add(domain);
                continue;
            }
            if (alertCategories.Contains(cat))
            {
                categoryAlert.Add(domain);
            }
        }

        // Adult toggle (best effort) adds a small explicit adult list
        if (policy.WebAdultBlockEnabled)
        {
            foreach (var d in _categoryEngine.GetAdultDomains())
                categoryBlocked.Add(d);
        }

        // Final blocked set: explicit blocked + category blocked + category alert - allow exceptions
        var finalBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in block) finalBlocked.Add(d);
        foreach (var d in categoryBlocked) finalBlocked.Add(d);
        foreach (var d in categoryAlert) finalBlocked.Add(d);
        foreach (var d in allow) finalBlocked.Remove(d);

        // Attach reasons per domain (best effort) for parent troubleshooting.
        var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in finalBlocked)
        {
            if (categoryAlert.Contains(d)) reasons[d] = "category_alert";
            else if (categoryBlocked.Contains(d)) reasons[d] = "category_block";
            else reasons[d] = "explicit_block";
        }
        _blockServer.SetDomainReasons(reasons);

        // Apply to hosts file only if it changed.
        var applied = true;
        var hash = ComputeStableHash(finalBlocked);
        if (!string.Equals(hash, _lastAppliedHash, StringComparison.Ordinal))
        {
            applied = _hosts.TryApply(finalBlocked, out var _);
            _lastAppliedHash = hash;
        }

        // Web aggregates from block server (counts HTTP hits to 127.0.0.1).
        // Use a reset-by-heartbeat snapshot so counts represent the interval and don't grow unbounded.
        var blockedItems = _blockServer.TakeTopBlockedDomainsAndReset(maxItems: 8);

        // AlertsToday counts "Alert"-classified hits (best effort).
        var alertCount = blockedItems.Where(i => string.Equals(i.Reason, "category_alert", StringComparison.OrdinalIgnoreCase))
            .Sum(i => i.Count);

        var report = new WebReport(
            LocalDate: LocalDateString(nowUtc.ToLocalTime()),
            BlockedDomainsConfigured: finalBlocked.Count,
            TopBlockedDomains: blockedItems,
            AlertsToday: alertCount
        );

        var circ = _circDetector.Detect(policy.WebCircumventionDetectionEnabled, applied ? null : "hosts_write_failed");
        if (!applied)
        {
            circ = circ with { HostsWriteFailed = true };
        }

        return (report, circ);
    }

    private static IEnumerable<string> NormalizeDomains(string[]? domains)
        => (domains ?? Array.Empty<string>())
            .Select(d => (d ?? string.Empty).Trim().ToLowerInvariant())
            .Where(d => d.Length > 0)
            .Select(DomainCategoryEngine.NormalizeDomain)
            .Where(d => d.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static string ComputeStableHash(HashSet<string> set)
    {
        // stable across runs
        var ordered = set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray();
        var joined = string.Join("|", ordered);
        return joined.GetHashCode().ToString("X");
    }

    private static string LocalDateString(DateTimeOffset local) => local.ToString("yyyy-MM-dd");
}
