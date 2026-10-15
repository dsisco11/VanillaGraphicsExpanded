using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns the procedural solar executable without borrowing native shader or texture state.</summary>
[ShaderProgram("Contract", "celestial_sun", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "celestial_sun.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "celestial_sun.fsh")]
internal sealed partial class SunProgram : GpuProgram, ISunBindings
{
    internal readonly SunInputs DrawInputs;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? frameLightingInputs;
    #region Public API
    /// <summary>Registers retained disk inputs and explicit frame bindings.</summary>
    public SunProgram()
    {
        DrawInputs = OwnUniformBuffer(new SunInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Identifies the owned offline shader contract.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Supplies an alternate camera for fixture or offscreen rendering.</summary>
    internal VgeFrameUniformBuffer? Frame { get => frame; set { RequireInputMutation(); frame = value; } }
    /// <summary>Borrows the physical storage publication for an alternate view.</summary>
    internal GpuShaderStorageBuffer? FrameLightingInputs
    {
        get => frameLightingInputs;
        set { RequireInputMutation(); frameLightingInputs = value; }
    }
    /// <summary>Resolves the frozen physical-light scale for the admitted view.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting =>
        frameLightingInputs ?? CameraExposure.CameraExposureRenderer.FrozenLighting;
    /// <summary>Resolves the camera without taking ownership of its lifecycle.</summary>
    CpuUniformBuffer ISunBindings.FrameInputs => Frame ?? VgeFrameRenderer.Current;
    /// <summary>Publishes the retained disk parameters.</summary>
    CpuUniformBuffer ISunBindings.Inputs => DrawInputs;
    #endregion
}
