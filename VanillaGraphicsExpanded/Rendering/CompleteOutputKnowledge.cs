using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Independent knowledge for supplemental output parameters.</summary>
[Flags]
internal enum CompleteOutputKnowledge
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed LogicOperation value.</summary>
    LogicOperation = 1 << 0,
}
