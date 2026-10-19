using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;

/// <summary>Declares synchronous descriptor observation and GPU-owned scheduling resources.</summary>
internal interface ILightingVolumeWorkBindings
{
    #region Public API
    /// <summary>Supplies bounded window offsets, frozen demand, frame identity and native-local frustum planes.</summary>
    [ShaderBinding("LightingWorkInputs",ShaderBindingKind.UniformBlock,0,ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Borrows only the certified current native descriptor image.</summary>
    [ShaderBinding("workMap",ShaderBindingKind.Sampler,0,ShaderStageKind.Compute,Required=false,Sampler=ShaderSamplerPolicy.NearestClamp)]
    GpuTexture? Map { set; }
    /// <summary>Updates the exact circular previous-observation record in place.</summary>
    [ShaderBinding("workPrevious",ShaderBindingKind.Image,0,ShaderStageKind.Compute,Required=false)]
    GpuTextureBinding Previous { set; }
    /// <summary>Updates the exact circular cumulative hard-change reference in place.</summary>
    [ShaderBinding("workReference",ShaderBindingKind.Image,1,ShaderStageKind.Compute,Required=false)]
    GpuTextureBinding Reference { set; }
    /// <summary>Updates only node-owned alpha masks outside all lighting readers.</summary>
    [ShaderBinding("workNodes",ShaderBindingKind.Image,2,ShaderStageKind.Compute,Required=false,TextureTarget=ShaderTextureTarget.Texture3D)]
    GpuTextureBinding Nodes { set; }
    /// <summary>Retains exact region ages, revisions and coalesced demand.</summary>
    [ShaderBinding("WorkRegions",ShaderBindingKind.StorageBlock,0,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Regions { set; }
    /// <summary>Retains bounded output lists, counters, fair cursors and a reusable priority heap.</summary>
    [ShaderBinding("WorkScratch",ShaderBindingKind.StorageBlock,1,ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Scratch { set; }
    #endregion
}
