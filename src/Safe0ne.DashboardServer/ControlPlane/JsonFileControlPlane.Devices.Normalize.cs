#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    /// <summary>
    /// Normalizes paired device records after deserialization / schema drift.
    /// 
    /// IMPORTANT:
    /// - Must be called under <see cref="_gate"/>.
    /// - Must not throw (best-effort normalization only).
    /// </summary>
    private void NormalizePairedDevicesUnsafe_NoLock()
    {
        try
        {
            if (_devicesByChildGuid is null || _devicesByChildGuid.Count == 0)
                return;

            foreach (var key in _devicesByChildGuid.Keys.ToList())
            {
                var devices = _devicesByChildGuid[key];
                if (devices is null || devices.Count == 0) continue;

                // Defensive: drop null entries and de-dup by DeviceId (keep most recent PairedAtUtc).
                var cleaned = devices
                    .Where(d => d is not null)
                    .GroupBy(d => d.DeviceId)
                    .Select(g => g.OrderByDescending(x => x.PairedAtUtc).First())
                    .ToList();

                // Defensive: if any device has an empty hash (older schema), generate a non-empty placeholder
                // so auth logic never crashes. This does NOT mint a usable token; it only stabilizes storage.
                for (var idx = 0; idx < cleaned.Count; idx++)
                {
                    var device = cleaned[idx];
                    if (string.IsNullOrWhiteSpace(device.TokenHashSha256))
                    {
                        // Placeholder hash derived from device id.
                        // NOTE: This does NOT mint a usable token; it only stabilizes storage.
                        var placeholder = ComputeSha256Hex(device.DeviceId.ToString("N"));
                        cleaned[idx] = new PairedDevice(
                            DeviceId: device.DeviceId,
                            DeviceName: device.DeviceName,
                            AgentVersion: device.AgentVersion,
                            PairedAtUtc: device.PairedAtUtc,
                            TokenHashSha256: placeholder,
                            LastSeenUtc: device.LastSeenUtc,
                            TokenIssuedAtUtc: device.TokenIssuedAtUtc ?? device.PairedAtUtc,
                            TokenExpiresAtUtc: device.TokenExpiresAtUtc,
                            TokenRevokedAtUtc: device.TokenRevokedAtUtc,
                            TokenRevokedBy: device.TokenRevokedBy,
                            TokenRevokedReason: device.TokenRevokedReason);
                    }
                }

                // Normalize token metadata fields for schema drift.
                // NOTE: TokenExpiresAtUtc may be null in older states; compute it based on issuedAt + TTL.
                for (var idx = 0; idx < cleaned.Count; idx++)
                {
                    var device = cleaned[idx];
                    var issuedAt = device.TokenIssuedAtUtc ?? device.PairedAtUtc;
                    var expiresAt = device.TokenExpiresAtUtc ?? ComputeDeviceTokenExpiresAt(issuedAt);

                    // Clamp expiry to revoke time if revoked.
                    if (device.TokenRevokedAtUtc.HasValue && expiresAt > device.TokenRevokedAtUtc.Value)
                    {
                        expiresAt = device.TokenRevokedAtUtc.Value;
                    }

                    cleaned[idx] = device with { TokenIssuedAtUtc = issuedAt, TokenExpiresAtUtc = expiresAt };
                }

                _devicesByChildGuid[key] = cleaned;
            }
        }
        catch
        {
            // best-effort only
        }
    }
}
