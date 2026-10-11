using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Declares the compute camera metering pass inputs.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface ICameraAdaptShaderProgramBindings
{
    #region Public API
    /// <summary>Supplies the shared frame duration for temporal adaptation.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Compute)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies metering and adaptation parameters.</summary>
    [ShaderBinding("CameraExposureInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies independently written workgroup histograms.</summary>
    [ShaderBinding("CameraHistogram", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Histogram { set; }
    /// <summary>Supplies the previous successfully published exposure.</summary>
    [ShaderBinding("PreviousExposure", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? PreviousExposure { set; }
    /// <summary>Receives the next exposure.</summary>
    [ShaderBinding("OutputExposure", ShaderBindingKind.StorageBlock, 2, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? OutputExposure { set; }
    #endregion
}
