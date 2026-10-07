namespace VanillaGraphicsExpanded.Rendering.Pipeline;

/// <summary>Draw range and instance count, independent of immutable pipeline identity.</summary>
/// <remarks>Array adapters interpret the range as vertices; pool adapters interpret it as validated index groups.</remarks>
internal readonly record struct GraphicsDraw(int FirstIndex, int IndexCount, int BaseVertex = 0, int InstanceCount = 1);
