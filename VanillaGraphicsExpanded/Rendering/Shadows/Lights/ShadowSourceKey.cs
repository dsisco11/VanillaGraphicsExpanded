namespace VanillaGraphicsExpanded.Rendering.Shadows.Lights;

/// <summary>Identifies an emission independently of position, frame ordering and shadow admission.</summary>
internal readonly record struct ShadowSourceKey(long World, int Dimension, ShadowSourceKind Kind,
    long Owner, long Lifetime, int Slot, int X = 0, int Y = 0, int Z = 0)
{
    /// <summary>Default keys deliberately cannot own retained shadow results.</summary>
    internal bool IsKnown => World > 0 && Lifetime > 0 && Kind != ShadowSourceKind.Unknown;
}
