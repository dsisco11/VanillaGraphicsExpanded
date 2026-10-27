namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes direct nonindexed work independently of the selected topology and resource bindings.</summary>
public readonly record struct RHIDraw(int FirstVertex, int VertexCount, int InstanceCount = 1, uint BaseInstance = 0);
