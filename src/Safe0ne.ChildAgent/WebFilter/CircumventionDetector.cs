
using System.Net.NetworkInformation;
using Microsoft.Win32;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.ChildAgent.WebFilter;

internal sealed class CircumventionDetector
{
    public CircumventionSignals Detect(bool enabled, string? extraNote)
    {
        if (!enabled)
        {
            return new CircumventionSignals(false, false, false, false, Notes(extraNote, "detection_disabled"));
        }

        var vpn = DetectVpn();
        var proxy = DetectProxyEnabled();
        var publicDns = DetectPublicDns();

        return new CircumventionSignals(
            VpnSuspected: vpn,
            ProxyEnabled: proxy,
            PublicDnsDetected: publicDns,
            HostsWriteFailed: false,
            Notes: Notes(extraNote, null));
    }

    private static bool DetectVpn()
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel || ni.NetworkInterfaceType == NetworkInterfaceType.Ppp)
                    return true;

                var name = (ni.Name ?? string.Empty).ToLowerInvariant();
                var desc = (ni.Description ?? string.Empty).ToLowerInvariant();
                if (name.Contains("vpn") || desc.Contains("vpn") || name.Contains("wireguard") || desc.Contains("wireguard") || name.Contains("openvpn") || desc.Contains("openvpn"))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static bool DetectProxyEnabled()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            var v = key?.GetValue("ProxyEnable");
            if (v is int i) return i != 0;
            if (v is byte[] b && b.Length > 0) return b[0] != 0;
        }
        catch { }
        return false;
    }

    private static bool DetectPublicDns()
    {
        try
        {
            var publicResolvers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "8.8.8.8", "8.8.4.4", // Google
                "1.1.1.1", "1.0.0.1", // Cloudflare
                "9.9.9.9", // Quad9
                "208.67.222.222", "208.67.220.220" // OpenDNS
            };

            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                var ipProps = ni.GetIPProperties();
                foreach (var dns in ipProps.DnsAddresses)
                {
                    var s = dns.ToString();
                    if (publicResolvers.Contains(s))
                        return true;
                }
            }
        }
        catch { }
        return false;
    }

    private static string[]? Notes(string? a, string? b)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(a)) list.Add(a);
        if (!string.IsNullOrWhiteSpace(b)) list.Add(b);
        return list.Count == 0 ? null : list.ToArray();
    }
}