using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Shares celestial rotation, atmospheric transmission and frozen scene storage with both draws.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(CameraExposure.IPhotometricFrameBindings), Defaults = true)]
internal interface ICelestialDrawBindings : CameraExposure.IPhotometricFrameBindings
{
    /// <summary>Supplies the shared perspective view and viewport.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies one matching source orientation and encoding.</summary>
    [ShaderBinding("CelestialDrawInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Samples only the VGE-generated HDR map.</summary>
    [ShaderBinding("galaxyMap", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.TextureCubeMap, Required = false)]
    GpuTexture? GalaxyMap { set; }
    /// <summary>Samples cached existing atmospheric extinction, without integrating inside the draw.</summary>
    [ShaderBinding("externalTransmission", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTexture? ExternalTransmission { set; }
}

