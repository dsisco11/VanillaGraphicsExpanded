using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.LumOn.Scene.Shaders;

/// <summary>Owns shader declarations for this packaged source or fixture.</summary>
[ShaderProgram("Contract", "lumonscene_reset_irradiance", 1)]
[ShaderStage("Contract", ShaderStageKind.Compute, "lumonscene_reset_irradiance.csh")]
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IShaderIncludeBindings), Defaults = true)]
internal sealed partial class LumonSceneResetIrradianceComputeShader : GpuComputeShader, ILumonSceneResetIrradianceComputeShaderBindings
{
    /// <summary>Adopts the executable and its retained resource contract.</summary>
    public LumonSceneResetIrradianceComputeShader(GpuComputePipeline pipeline) : base(pipeline) { }
}
