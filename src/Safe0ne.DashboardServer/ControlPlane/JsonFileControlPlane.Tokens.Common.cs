using System;
using System.Security.Cryptography;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Tokens domain: shared helpers (SSOT).
///
/// ADR-0004: ControlPlane partials by domain.
/// Guardrail: shared helpers for the Tokens domain live here.
/// </summary>
public sealed partial class JsonFileControlPlane
{
    /// <summary>
    /// Generates a new opaque device token.
    /// </summary>
    private static string GenerateDeviceToken()
    {
        // 32 bytes => 256-bit token.
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Gets the TTL for device tokens.
    /// Defaults to 30 days, with environment variable overrides.
    /// </summary>
    private static TimeSpan GetDeviceTokenTtl()
    {
        // Pairing hardening: allow token TTL override for testing and future policy.
        // Defaults to 30 days.
        // Env (first match wins):
        //  - SAFE0NE_DEVICE_TOKEN_TTL_SECONDS
        //  - SAFE0NE_DEVICE_TOKEN_TTL_MINUTES
        //  - SAFE0NE_DEVICE_TOKEN_TTL_DAYS
        try
        {
            var rawSeconds = Environment.GetEnvironmentVariable("SAFE0NE_DEVICE_TOKEN_TTL_SECONDS");
            if (!string.IsNullOrWhiteSpace(rawSeconds) && int.TryParse(rawSeconds, out var s) && s > 0)
            {
                return TimeSpan.FromSeconds(s);
            }

            var rawMinutes = Environment.GetEnvironmentVariable("SAFE0NE_DEVICE_TOKEN_TTL_MINUTES");
            if (!string.IsNullOrWhiteSpace(rawMinutes) && int.TryParse(rawMinutes, out var m) && m > 0)
            {
                return TimeSpan.FromMinutes(m);
            }

            var rawDays = Environment.GetEnvironmentVariable("SAFE0NE_DEVICE_TOKEN_TTL_DAYS");
            if (!string.IsNullOrWhiteSpace(rawDays) && int.TryParse(rawDays, out var d) && d > 0)
            {
                return TimeSpan.FromDays(d);
            }
        }
        catch
        {
            // ignore
        }

        return TimeSpan.FromDays(30);
    }

    /// <summary>
    /// Computes the token expiry instant based on issuance time and configured TTL.
    /// </summary>
    private static DateTimeOffset ComputeDeviceTokenExpiresAt(DateTimeOffset issuedAtUtc)
        => issuedAtUtc.Add(GetDeviceTokenTtl());

    /// <summary>
    /// Determines whether a token is expired. Null expiry is treated as non-expired
    /// (back-compat with older persisted snapshots).
    /// </summary>
    private static bool IsDeviceTokenExpired(DateTimeOffset nowUtc, DateTimeOffset? expiresAtUtc)
        => expiresAtUtc is not null && expiresAtUtc.Value <= nowUtc;

    /// <summary>
    /// Computes SHA-256 hex digest for token hashing.
    /// </summary>
    private static string ComputeSha256Hex(string input)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }
}
