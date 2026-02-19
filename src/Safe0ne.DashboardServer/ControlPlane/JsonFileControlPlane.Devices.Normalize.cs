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
                for (var i = 0; i < cleaned.Count; i++)
                {
                    var d = cleaned[i];
                    if (string.IsNullOrWhiteSpace(d.TokenHashSha256))
                    {
                        // placeholder hash derived from device id
                        var placeholder = ComputeSha256Hex(d.DeviceId.ToString("N"));
                        cleaned[i] = new PairedDevice(
                            DeviceId: d.DeviceId,
                            DeviceName: d.DeviceName,
                            AgentVersion: d.AgentVersion,
                            PairedAtUtc: d.PairedAtUtc,
                            TokenHashSha256: placeholder);
                    }
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
