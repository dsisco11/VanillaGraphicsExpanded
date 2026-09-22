using VanillaGraphicsExpanded.Rendering.Contracts;
using static VanillaGraphicsExpanded.Rendering.Contracts.LumOnShaderGroups;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns the immutable shader declarations shared with offline compilation.</summary>
internal partial class LumonSceneRelightVoxelDdaComputeShader
{
    #region Shader contracts
    /// <summary>Immutable declaration for the lumonscene_relight_voxel_dda program.</summary>
    internal static GpuShaderContract Contract { get; } = ShaderProgramDeclaration.Compute("lumonscene_relight_voxel_dda", "lumonscene_relight_voxel_dda.csh");
    #endregion
}
