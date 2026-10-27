namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Specifies an immutable query category and optional diagnostic label.</summary>
public readonly record struct RHIQueryDescription(QueryKind Kind, string? Label = null);
