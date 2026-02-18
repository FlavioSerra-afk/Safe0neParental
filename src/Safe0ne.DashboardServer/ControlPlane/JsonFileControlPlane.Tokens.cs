using System.Security.Cryptography;
using System.Text;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Crypto + token helpers for JsonFileControlPlane.
/// Split into a dedicated partial to keep JsonFileControlPlane.cs focused on persistence + domain logic.
/// </summary>
public sealed partial class JsonFileControlPlane
{
    private static string GenerateDeviceToken()
    {
        // 256-bit random token -> hex string
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static TimeSpan GetDeviceTokenTtl() => TimeSpan.FromDays(30);

    private static string ComputeSha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
