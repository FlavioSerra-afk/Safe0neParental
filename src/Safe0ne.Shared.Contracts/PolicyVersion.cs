namespace Safe0ne.Shared.Contracts;

/// <summary>
/// Monotonic policy version used for sync and cache validation.
/// </summary>
public readonly record struct PolicyVersion(long Value)
{
    public static readonly PolicyVersion Initial = new(1);

    public PolicyVersion Next() => new(Value + 1);

    public override string ToString() => Value.ToString();
}
