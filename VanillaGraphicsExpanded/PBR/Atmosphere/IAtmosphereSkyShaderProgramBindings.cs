using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Declares sky inputs shared by the offline compiler and prepared runtime submissions.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IAtmosphereSkyShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies the shared camera and frame dimensions.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies view-ray transforms, atmospheric direction and engine spatial effects.</summary>
    [ShaderBinding("SkyInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Uses the lookup texture's repeat-U and clamp-V filtering contract.</summary>
    [ShaderBinding("skyLookup", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D)]
    int SkyLookup { set; }
    /// <summary>Uses the engine liquid-depth sampler with explicit nearest edge-clamped reads.</summary>
    [ShaderBinding("liquidDepth", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    int LiquidDepth { set; }
    #endregion
}
