using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Independent knowledge for supplemental dynamic parameters.</summary>
[Flags]
internal enum CompleteDynamicKnowledge
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed Scissor value.</summary>
    Scissor = 1 << 0,
    /// <summary>Declared or observed BlendConstant value.</summary>
    BlendConstant = 1 << 1,
}
