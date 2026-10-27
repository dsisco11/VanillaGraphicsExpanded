namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Describes an index interval with signed vertex translation and explicit instancing.</summary>
public readonly record struct RHIDrawIndexed(int FirstIndex, int IndexCount, int BaseVertex = 0,
    int InstanceCount = 1, uint BaseInstance = 0);
