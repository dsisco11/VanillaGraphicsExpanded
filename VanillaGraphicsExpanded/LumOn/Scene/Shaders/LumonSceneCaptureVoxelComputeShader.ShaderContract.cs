using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal partial class LumonSceneCaptureVoxelComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumonscene_capture_voxel program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("lumonscene_capture_voxel", "lumonscene_capture_voxel.csh");
    #endregion
}
