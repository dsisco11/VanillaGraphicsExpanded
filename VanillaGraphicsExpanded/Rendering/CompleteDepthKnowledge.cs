using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Independent knowledge for supplemental depth parameters.</summary>
[Flags]
internal enum CompleteDepthKnowledge
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed DepthRange value.</summary>
    DepthRange = 1 << 0,
}
