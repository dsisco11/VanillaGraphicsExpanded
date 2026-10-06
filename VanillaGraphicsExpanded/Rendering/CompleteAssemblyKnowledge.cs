using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Independent knowledge for supplemental assembly parameters.</summary>
[Flags]
internal enum CompleteAssemblyKnowledge
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed RestartIndex value.</summary>
    RestartIndex = 1 << 0,
}
