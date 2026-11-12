namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>One owner-supplied non-terrain draw, independent of terrain membership and physical shadow pages.</summary>
/// <remarks>Unknown deformation bounds deliberately select the complete draw in each view. Original
/// instance streams remain bound by the native adapter, with base-instance zero and a view-local selector.</remarks>
internal readonly record struct ShadowObjectDrawRange(uint Command, uint IndexStart, uint IndexCount,
    int BaseVertex, uint Instances, uint TriangleCost, uint Object, int Dimension = 0);
