namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Stable identifier for a child profile.
/// 
/// Notes:
/// - This is intentionally a small, stable value-object wrapper over <see cref="Guid"/>.
/// - For ASP.NET Core minimal APIs and route binding, implement Parse/TryParse patterns.
/// </summary>
public readonly record struct ChildId(Guid Value)
{
    public static ChildId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();

    // --- Parsing helpers (Minimal API / model binding) ---

    public static ChildId Parse(string s) => new(Guid.Parse(s));

    public static ChildId Parse(string s, IFormatProvider? provider) => new(Guid.Parse(s));

    public static bool TryParse(string? s, out ChildId result)
    {
        if (Guid.TryParse(s, out var g))
        {
            result = new ChildId(g);
            return true;
        }

        result = default;
        return false;
    }

    public static bool TryParse(string? s, IFormatProvider? provider, out ChildId result)
        => TryParse(s, out result);

    public static bool TryParse(ReadOnlySpan<char> s, out ChildId result)
    {
        if (Guid.TryParse(s, out var g))
        {
            result = new ChildId(g);
            return true;
        }

        result = default;
        return false;
    }

    public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider, out ChildId result)
        => TryParse(s, out result);
}
