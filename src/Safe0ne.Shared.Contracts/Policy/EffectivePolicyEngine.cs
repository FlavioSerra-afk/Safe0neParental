using System.Globalization;

namespace Safe0ne.Shared.Contracts.Policy;

/// <summary>
/// Deterministic policy evaluator used by both Server and Kid (pure function).
///
/// Precedence (SSOT): AlwaysAllowed &gt; Grants &gt; Mode &gt; Schedules/Budgets
///
/// This engine is intentionally conservative: schedules can only INCREASE restriction
/// relative to the configured mode, never decrease it.
///
/// NOTE: v1 assumes Parent + Kid share the same local timezone; schedule evaluation is
/// performed in local time.
/// </summary>
public static class EffectivePolicyEngine
{
    public static EffectiveChildState Evaluate(ChildPolicy policy, DateTimeOffset nowUtc)
    {
        if (policy.AlwaysAllowed)
        {
            return new EffectiveChildState(
                ChildId: policy.ChildId,
                PolicyVersion: policy.Version,
                ConfiguredMode: policy.Mode,
                EffectiveMode: SafetyMode.Open,
                ReasonCode: "always_allowed",
                EvaluatedAtUtc: nowUtc,
                GrantUntilUtc: policy.GrantUntilUtc,
                AlwaysAllowed: policy.AlwaysAllowed,
                ActiveSchedule: null);
        }

        if (policy.GrantUntilUtc is not null && nowUtc < policy.GrantUntilUtc.Value)
        {
            return new EffectiveChildState(
                ChildId: policy.ChildId,
                PolicyVersion: policy.Version,
                ConfiguredMode: policy.Mode,
                EffectiveMode: SafetyMode.Open,
                ReasonCode: "grant",
                EvaluatedAtUtc: nowUtc,
                GrantUntilUtc: policy.GrantUntilUtc,
                AlwaysAllowed: policy.AlwaysAllowed,
                ActiveSchedule: null);
        }

        // Start with configured mode.
        var baseMode = policy.Mode;
        var effectiveMode = baseMode;
        var reason = "mode";
        string? activeSchedule = null;

        // Apply schedules last (they can only increase restriction).
        var (schedMode, schedName) = EvaluateSchedules(policy, nowUtc);
        if (schedMode != SafetyMode.Open)
        {
            var merged = MaxRestrictive(baseMode, schedMode);
            if (merged != baseMode)
            {
                effectiveMode = merged;
                reason = "schedule";
                activeSchedule = schedName;
            }
        }

        return new EffectiveChildState(
            ChildId: policy.ChildId,
            PolicyVersion: policy.Version,
            ConfiguredMode: policy.Mode,
            EffectiveMode: effectiveMode,
            ReasonCode: reason,
            EvaluatedAtUtc: nowUtc,
            GrantUntilUtc: policy.GrantUntilUtc,
            AlwaysAllowed: policy.AlwaysAllowed,
            ActiveSchedule: activeSchedule);
    }

    private static (SafetyMode Mode, string? Name) EvaluateSchedules(ChildPolicy policy, DateTimeOffset nowUtc)
    {
        var local = nowUtc.ToLocalTime();
        var tod = local.TimeOfDay;

        SafetyMode mode = SafetyMode.Open;
        string? name = null;

        // Bedtime
        if (IsActive(policy.BedtimeWindow, tod))
        {
            mode = MaxRestrictive(mode, SafetyMode.Bedtime);
            name = "bedtime";
        }

        // School time (treated as Homework restriction level in v1)
        if (IsActive(policy.SchoolWindow, tod))
        {
            var merged = MaxRestrictive(mode, SafetyMode.Homework);
            if (merged != mode) name = "school";
            mode = merged;
        }

        // Homework time
        if (IsActive(policy.HomeworkWindow, tod))
        {
            var merged = MaxRestrictive(mode, SafetyMode.Homework);
            if (merged != mode) name = "homework";
            mode = merged;
        }

        return (mode, name);
    }

    private static bool IsActive(ScheduleWindow? w, TimeSpan nowTod)
    {
        if (w is null || !w.Enabled) return false;
        if (!TryParseLocalTime(w.StartLocal, out var start)) return false;
        if (!TryParseLocalTime(w.EndLocal, out var end)) return false;

        if (start == end) return false;

        // Same-day window
        if (start < end)
        {
            return nowTod >= start && nowTod < end;
        }

        // Cross-midnight window
        return nowTod >= start || nowTod < end;
    }

    private static bool TryParseLocalTime(string? s, out TimeSpan time)
    {
        // Accept "HH:mm" (docs) and be lenient.
        if (TimeSpan.TryParseExact(s ?? string.Empty, "g", CultureInfo.InvariantCulture, out time))
            return true;
        if (TimeSpan.TryParse(s, CultureInfo.InvariantCulture, out time))
            return true;
        time = default;
        return false;
    }

    private static SafetyMode MaxRestrictive(SafetyMode a, SafetyMode b)
        => (SafetyMode)Math.Max((int)a, (int)b);
}
