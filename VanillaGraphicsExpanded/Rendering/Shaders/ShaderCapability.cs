using System;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Features explicitly installed by VGE shader patching or executable replacement.</summary>
[Flags]
internal enum ShaderCapability
{
    /// <summary>No VGE shader capabilities are declared.</summary>
    None = 0,
    /// <summary>Supports VGE's two-sided surface-normal handling.</summary>
    TwoSidedSurfaceNormals = 1,
    /// <summary>Uses VGE's tessellated terrain displacement executable.</summary>
    TerrainDisplacement = 2,
    /// <summary>Supports VGE's selectable scene color convention; whole-frame readiness is a separate requirement.</summary>
    SceneColorConvention = 4,
    /// <summary>Publishes linear material inputs for VGE deferred lighting rather than completed scene color.</summary>
    SceneMaterialCapture = 8
}
