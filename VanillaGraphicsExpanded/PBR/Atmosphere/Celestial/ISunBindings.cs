using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Declares the owned solar draw's camera, disk and physical frame-storage inputs.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(CameraExposure.IPhotometricFrameBindings), Defaults = true)]
internal interface ISunBindings : CameraExposure.IPhotometricFrameBindings
{
    #region Public API
    /// <summary>Supplies the translation-independent directional camera projection.</summary>
    [ShaderBinding("VgeFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies the admitted solar geometry, radiance and CPU storage limit.</summary>
    [ShaderBinding("SunInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    #endregion
}
