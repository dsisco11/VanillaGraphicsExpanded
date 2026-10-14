using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Declares camera, physical storage and procedural lunar inputs with no native image bindings.</summary>
[ShaderBindingSet(typeof(IShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(CameraExposure.IPhotometricFrameBindings), Defaults = true)]
internal interface IMoonBindings : CameraExposure.IPhotometricFrameBindings
{
    /// <summary>Supplies the perspective view used to reconstruct lunar directions.</summary>
    [ShaderBinding("VgeFrameUBO",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.Frame,ShaderStageKind.Fragment)]
    CpuUniformBuffer FrameInputs { get; }
    /// <summary>Supplies the CPU-calibrated disk from one completed atmospheric publication.</summary>
    [ShaderBinding("MoonInputs",ShaderBindingKind.UniformBlock,GpuBindingRegistry.Ubo.ShaderInputs,ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
}
