using System;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.Rendering.Integration;
/// <summary>Contains native-engine ownership checks at VGE's supported integration boundary.</summary>
internal static class NativeShaderHandoff
{
    #region Internal API
    /// <summary>Rejects active native shaders whose restoration footprint has not been declared.</summary>
    internal static void RequireInactive()
    {
        if (ShaderProgramBase.CurrentShaderProgram != null)
            throw new InvalidOperationException("An active native shader cannot be interrupted by this VGE scope.");
    }
    /// <summary>Reports whether the supported outer boundary has no active native owner.</summary>
    internal static bool IsInactive => ShaderProgramBase.CurrentShaderProgram == null;
    #endregion
}
