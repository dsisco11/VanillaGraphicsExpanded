using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Independent knowledge for supplemental raster parameters.</summary>
[Flags]
internal enum CompleteRasterKnowledge
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed CullMode value.</summary>
    CullMode = 1 << 0,
    /// <summary>Declared or observed FrontFace value.</summary>
    FrontFace = 1 << 1,
    /// <summary>Declared or observed PolygonModes value.</summary>
    PolygonModes = 1 << 2,
    /// <summary>Declared or observed PolygonOffset value.</summary>
    PolygonOffset = 1 << 3,
}
