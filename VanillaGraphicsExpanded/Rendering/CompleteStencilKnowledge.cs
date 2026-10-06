using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Independent knowledge for supplemental stencil parameters.</summary>
[Flags]
internal enum CompleteStencilKnowledge
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed FrontStencilFunction value.</summary>
    FrontStencilFunction = 1 << 0,
    /// <summary>Declared or observed BackStencilFunction value.</summary>
    BackStencilFunction = 1 << 1,
    /// <summary>Declared or observed FrontStencilMask value.</summary>
    FrontStencilMask = 1 << 2,
    /// <summary>Declared or observed BackStencilMask value.</summary>
    BackStencilMask = 1 << 3,
    /// <summary>Declared or observed FrontStencilOperation value.</summary>
    FrontStencilOperation = 1 << 4,
    /// <summary>Declared or observed BackStencilOperation value.</summary>
    BackStencilOperation = 1 << 5,
}
