using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal partial class LumonSceneCaptureMeshCardComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumonscene_capture_meshcard program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("lumonscene_capture_meshcard", "lumonscene_capture_meshcard.csh");
    #endregion
}
