
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.WebFilter;

/// <summary>
/// Very small, embedded domain->category map for prototype-only category blocking.
/// Unknown domains classify as Unknown.
/// </summary>
internal sealed class DomainCategoryEngine
{
    private readonly Dictionary<string, WebCategory> _map;

    public DomainCategoryEngine()
    {
        _map = new Dictionary<string, WebCategory>(StringComparer.OrdinalIgnoreCase)
        {
            // Social
            ["facebook.com"] = WebCategory.Social,
            ["instagram.com"] = WebCategory.Social,
            ["tiktok.com"] = WebCategory.Social,
            ["snapchat.com"] = WebCategory.Social,
            ["x.com"] = WebCategory.Social,
            ["twitter.com"] = WebCategory.Social,

            // Games
            ["roblox.com"] = WebCategory.Games,
            ["minecraft.net"] = WebCategory.Games,
            ["store.steampowered.com"] = WebCategory.Games,

            // Streaming
            ["youtube.com"] = WebCategory.Streaming,
            ["netflix.com"] = WebCategory.Streaming,
            ["twitch.tv"] = WebCategory.Streaming,
            ["disneyplus.com"] = WebCategory.Streaming,

            // Shopping
            ["amazon.com"] = WebCategory.Shopping,
            ["ebay.com"] = WebCategory.Shopping,
            ["aliexpress.com"] = WebCategory.Shopping,
        };
    }

    public IEnumerable<string> GetKnownDomains() => _map.Keys;

    public WebCategory Classify(string domain)
    {
        var d = NormalizeDomain(domain);
        if (d.Length == 0) return WebCategory.Unknown;
        if (_map.TryGetValue(d, out var c)) return c;

        // Basic suffix match for subdomains
        foreach (var kv in _map)
        {
            if (d.EndsWith("." + kv.Key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }

        // Adult list uses separate toggle; classify those as Adult
        if (GetAdultDomains().Contains(d, StringComparer.OrdinalIgnoreCase))
            return WebCategory.Adult;

        return WebCategory.Unknown;
    }

    public IEnumerable<string> GetAdultDomains()
    {
        // Keep list small for prototype; parents can always add custom blocked domains.
        return new[]
        {
            "pornhub.com",
            "xvideos.com",
            "xnxx.com"
        };
    }

    public static string NormalizeDomain(string input)
    {
        var d = (input ?? string.Empty).Trim().ToLowerInvariant();
        if (d.StartsWith("http://")) d = d["http://".Length..];
        if (d.StartsWith("https://")) d = d["https://".Length..];
        d = d.Trim('/');
        // Strip port
        var colon = d.IndexOf(':');
        if (colon >= 0) d = d[..colon];
        // Strip leading www.
        if (d.StartsWith("www.")) d = d[4..];
        return d;
    }
}
