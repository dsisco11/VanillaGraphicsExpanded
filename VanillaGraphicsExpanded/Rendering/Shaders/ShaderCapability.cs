using System;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Features explicitly installed by VGE shader patching or executable replacement.</summary>
[Flags]
internal enum ShaderCapability
{
    None = 0,
    TwoSidedSurfaceNormals = 1,
    TerrainDisplacement = 2
}
