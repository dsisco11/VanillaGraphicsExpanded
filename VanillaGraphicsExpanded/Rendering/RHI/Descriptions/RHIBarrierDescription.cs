namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Names the subsequent accesses requiring visibility of earlier GPU writes.</summary>
public readonly record struct RHIBarrierDescription(RHIAccess Access);
