using System;
using Safe0ne.Shared.Contracts;

namespace Safe0ne.DashboardServer.ControlPlane;

/// <summary>
/// Compatibility shims for pairing helpers that may be referenced from Program.cs.
///
/// If the core <see cref="JsonFileControlPlane"/> already implements these members,
/// the compiler will bind to the instance methods and these extensions will be ignored.
/// </summary>
internal static class JsonFileControlPlanePairingExtensions
{
    public static bool ClearPendingPairing(this JsonFileControlPlane cp, ChildId childId)
    {
        // If an instance method exists, reflection will find it and we will invoke it.
        // If not, we return false (no pending pairing removed).
        var mi = typeof(JsonFileControlPlane).GetMethod(
            "ClearPendingPairing",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
            binder: null,
            types: new[] { typeof(ChildId) },
            modifiers: null);

        if (mi is null)
        {
            return false;
        }

        try
        {
            var result = mi.Invoke(cp, new object[] { childId });
            return result is bool b && b;
        }
        catch
        {
            // Best-effort compatibility shim; never throw from here.
            return false;
        }
    }
}
