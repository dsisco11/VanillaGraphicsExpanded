using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Supplies frozen physical storage only to explicitly migrated scene shaders.</summary>
internal interface IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Uses the existing camera owner; requesting an unavailable publication fails before draw submission.</summary>
    [ShaderBinding("FrozenFrameLighting", ShaderBindingKind.StorageBlock, 3,
        ShaderStageKind.Fragment, ShaderStageKind.Compute)]
    GpuShaderStorageBuffer FrameLighting => CameraExposureRenderer.FrozenLighting;
    #endregion
}
