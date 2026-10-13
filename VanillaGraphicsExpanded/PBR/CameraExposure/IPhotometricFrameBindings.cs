using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Supplies frozen physical storage only to explicitly migrated scene shaders.</summary>
internal interface IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Uses the existing camera owner; requesting an unavailable publication fails before draw submission.</summary>
    [ShaderBinding("FrozenFrameLighting", ShaderBindingKind.StorageBlock, GpuBindingRegistry.Ssbo.FrameLighting,
        ShaderStageKind.Fragment, ShaderStageKind.Compute, Required = false)]
    GpuShaderStorageBuffer FrameLighting => CameraExposureRenderer.FrozenLighting;
    #endregion
}
