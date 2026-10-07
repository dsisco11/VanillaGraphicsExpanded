namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Indexed draw range and instance count, independent of immutable pipeline identity.</summary>
internal readonly record struct GraphicsDraw(int FirstIndex, int IndexCount, int BaseVertex = 0, int InstanceCount = 1);
