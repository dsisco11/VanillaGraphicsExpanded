using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns the procedural GLSL 330 lunar executable and its physical frame bindings.</summary>
[ShaderProgram("Contract","celestial_moon",1)]
[ShaderStage("Contract",ShaderStageKind.Vertex,"celestial_moon.vsh")]
[ShaderStage("Contract",ShaderStageKind.Fragment,"celestial_moon.fsh")]
internal sealed partial class MoonProgram : GpuProgram, IMoonBindings
{
    internal readonly MoonInputs DrawInputs;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? frameLightingInputs;
    #region Public API
    /// <summary>Registers explicit frame and disk contracts.</summary>
    public MoonProgram()
    {
        DrawInputs=OwnUniformBuffer(new MoonInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Identifies the owned offline program.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Supplies a fixture camera outside prepared submissions.</summary>
    internal VgeFrameUniformBuffer? Frame { get => frame; set { RequireInputMutation(); frame=value; } }
    /// <summary>Supplies alternate storage for offscreen views.</summary>
    internal GpuShaderStorageBuffer? FrameLightingInputs
    {
        get => frameLightingInputs;
        set { RequireInputMutation(); frameLightingInputs=value; }
    }
    /// <summary>Uses exactly the admitted physical-light storage for this view.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting =>
        frameLightingInputs ?? CameraExposure.CameraExposureRenderer.FrozenLighting;
    /// <summary>Returns the retained camera or world frame.</summary>
    CpuUniformBuffer IMoonBindings.FrameInputs => Frame ?? VgeFrameRenderer.Current;
    /// <summary>Returns the guarded disk uniform buffer.</summary>
    CpuUniformBuffer IMoonBindings.Inputs => DrawInputs;
    #endregion
}
