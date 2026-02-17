using System;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace Safe0ne.ChildAgent.ChildUx;

/// <summary>
/// K7/K8: Best-effort navigation helper for the local child UX.
/// 
/// Design constraints:
/// - Must not require admin (no URL ACL changes).
/// - Must never throw (called from enforcement loop).
/// - Must be throttled to avoid spamming.
/// </summary>
public static class ChildUxNavigation
{
    private const int DefaultPort = 8771;

    private static int ResolvePort()
    {
        var raw = Environment.GetEnvironmentVariable("SAFEONE_CHILDUX_PORT");
        return int.TryParse(raw, out var p) && p is > 0 and < 65536 ? p : DefaultPort;
    }

    private static string BaseUrl => $"http://127.0.0.1:{ResolvePort()}/";

    public static void TryOpenWarning(string kind, int minutesRemaining, ref DateTimeOffset lastUxNavigateUtc, ILogger logger)
    {
        try
        {
            // Throttle: one navigation per 20 seconds.
            var now = DateTimeOffset.UtcNow;
            if (now - lastUxNavigateUtc < TimeSpan.FromSeconds(20)) return;
            lastUxNavigateUtc = now;

            kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind.Length == 0) kind = "warning";

            var url = BaseUrl + "warning" +
                      $"?kind={WebUtility.UrlEncode(kind)}" +
                      $"&minutes={minutesRemaining}";

            // UseShellExecute opens default browser without requiring elevated privileges.
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Child UX navigation failed");
        }
    }

    public static void TryOpenBlocked(string kind, string target, string reason, ref DateTimeOffset lastUxNavigateUtc, ILogger logger)
    {
        try
        {
            // Throttle: one navigation per 20 seconds.
            var now = DateTimeOffset.UtcNow;
            if (now - lastUxNavigateUtc < TimeSpan.FromSeconds(20)) return;
            lastUxNavigateUtc = now;

            kind = (kind ?? string.Empty).Trim().ToLowerInvariant();
            if (kind.Length == 0) kind = "blocked";

            var url = BaseUrl + "blocked" +
                      $"?kind={WebUtility.UrlEncode(kind)}" +
                      $"&target={WebUtility.UrlEncode(target ?? string.Empty)}" +
                      $"&reason={WebUtility.UrlEncode(reason ?? string.Empty)}";

            // UseShellExecute opens default browser without requiring elevated privileges.
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Child UX navigation failed");
        }
    }

    public static void TryOpenToday(ref DateTimeOffset lastUxNavigateUtc, ILogger logger)
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (now - lastUxNavigateUtc < TimeSpan.FromSeconds(20)) return;
            lastUxNavigateUtc = now;

            Process.Start(new ProcessStartInfo
            {
                FileName = BaseUrl + "today",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Child UX navigation failed");
        }
    }
}
