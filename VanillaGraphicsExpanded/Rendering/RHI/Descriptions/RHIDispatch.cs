namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Supplies explicit compute workgroup counts independently of executable code or resource contents.</summary>
public readonly record struct RHIDispatch(int X, int Y = 1, int Z = 1);
