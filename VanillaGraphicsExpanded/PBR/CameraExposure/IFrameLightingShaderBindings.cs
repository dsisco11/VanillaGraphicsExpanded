using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Declares one frozen frame-storage publication from admitted CPU bounds and optional GPU exposure history.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
internal interface IFrameLightingShaderBindings
{
    #region Public API
    /// <summary>Supplies the bound, manual seed and matching frame/calibration identity.</summary>
    [ShaderBinding("FrameLightingInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Compute)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Reads only successfully published automatic exposure; startup/manual paths do not read this slot.</summary>
    [ShaderBinding("PreviousExposure", ShaderBindingKind.StorageBlock, 0, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer? PreviousExposure { set; }
    /// <summary>Publishes scale, inverse, bound, validity and frame/calibration identities.</summary>
    [ShaderBinding("FrozenFrameLighting", ShaderBindingKind.StorageBlock, 1, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer? Output { set; }
    #endregion
}
