using System;

namespace VanillaGraphicsExpanded.LumOn.LumonScene;

internal readonly struct LumonScenePatchId : IEquatable<LumonScenePatchId>
{
    public readonly int Value;

    public LumonScenePatchId(int value)
    {
        Value = value;
    }

    public bool Equals(LumonScenePatchId other)
        => Value == other.Value;

    public override bool Equals(object? obj)
        => obj is LumonScenePatchId other && Equals(other);

    public override int GetHashCode()
        => Value;

    public static bool operator ==(LumonScenePatchId left, LumonScenePatchId right)
        => left.Equals(right);

    public static bool operator !=(LumonScenePatchId left, LumonScenePatchId right)
        => !left.Equals(right);

    public override string ToString()
        => Value.ToString();
}
