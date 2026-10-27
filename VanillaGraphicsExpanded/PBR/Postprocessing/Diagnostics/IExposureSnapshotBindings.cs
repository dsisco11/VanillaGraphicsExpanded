using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Postprocessing.Diagnostics;

/// <summary>Declares the completed display inputs sampled by the on-demand exposure probe.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(IPhotometricFrameBindings), Defaults = true)]
internal interface IExposureSnapshotBindings : IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Supplies final display policy without modifying runtime settings.</summary>
    [ShaderBinding("ExposureSnapshotInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies the same pixel spacing as final display.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Compute)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Samples the completed pre-glare display scene.</summary>
    [ShaderBinding("sceneImage", ShaderBindingKind.Sampler, 0, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.LinearClamp)]
    GpuTextureRef Scene { set; }
    /// <summary>Samples the completed local-exposure base.</summary>
    [ShaderBinding("localBaseImage", ShaderBindingKind.Sampler, 1, ShaderStageKind.Compute, TextureTarget = ShaderTextureTarget.Texture2D, Sampler = ShaderSamplerPolicy.NearestClamp)]
    GpuTextureRef LocalBase { set; }
    /// <summary>Borrows the current camera history or explicitly neutral manual-mode metadata.</summary>
    [ShaderBinding("SnapshotHistory", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer History { set; }
    /// <summary>Receives one coherent camera and centre-pixel record.</summary>
    [ShaderBinding("SnapshotOutput", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer Output { set; }
    #endregion
}
