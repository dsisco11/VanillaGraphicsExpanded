using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;
/// <summary>Declares every retained input consumed by LumonSceneResetIrradianceComputeShader.</summary>
internal interface ILumonSceneResetIrradianceComputeShaderBindings
{
    #region Public API
    /// <summary>Retains irradianceAtlas until submission.</summary>
    [ShaderBinding("irradianceAtlas", ShaderBindingKind.Image, 0, ShaderStageKind.Compute)]
    GpuTextureBinding irradianceAtlas { set; }
    #endregion
}
