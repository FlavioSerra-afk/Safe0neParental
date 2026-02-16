namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Stable identifier for a child profile.
/// </summary>
public readonly record struct ChildId(Guid Value)
{
    public static ChildId New() => new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}
