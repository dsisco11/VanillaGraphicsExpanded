using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Declares typed inputs for source rasterization and solid-angle mip reduction.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface ICelestialGenerationBindings
{
    /// <summary>Supplies one immutable tile's geometry, encoding and source calibration.</summary>
    [ShaderBinding("CelestialGenerationInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies directions and normalized residual flux; no native sky textures are accepted.</summary>
    [ShaderBinding("catalog", ShaderBindingKind.Sampler, 0, ShaderStageKind.Fragment)]
    GpuTextureRef Catalog { set; }
    /// <summary>Supplies bounded tile-local omitted-star indices.</summary>
    [ShaderBinding("indices", ShaderBindingKind.Sampler, 1, ShaderStageKind.Fragment)]
    GpuTextureRef Indices { set; }
    /// <summary>Reads a completed parent mip while writing separate scratch storage.</summary>
    [ShaderBinding("parentMap", ShaderBindingKind.Sampler, 2, ShaderStageKind.Fragment, TextureTarget = ShaderTextureTarget.TextureCubeMap)]
    GpuTextureRef ParentMap { set; }
}
