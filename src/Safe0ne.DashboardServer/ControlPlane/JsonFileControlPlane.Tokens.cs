// Canonical token helpers (Tokens domain partial)
//
// This file was previously a placeholder during the ControlPlane partial split.
// Keep helpers here additive-only to avoid churn in endpoint signatures.

using System;

namespace Safe0ne.DashboardServer.ControlPlane;

public sealed partial class JsonFileControlPlane
{
    // Default token lifetime for paired devices.
    // NOTE: This is a control-plane concern (server-side enforcement). The token itself is only stored as a hash.
    internal static readonly TimeSpan DefaultDeviceTokenTtl = TimeSpan.FromDays(30);

    internal static DateTimeOffset ComputeDeviceTokenExpiresAt(DateTimeOffset issuedAtUtc)
        => issuedAtUtc.Add(DefaultDeviceTokenTtl);

    internal static bool IsDeviceTokenExpired(DateTimeOffset nowUtc, DateTimeOffset? expiresAtUtc)
        => expiresAtUtc.HasValue && nowUtc > expiresAtUtc.Value;
}
