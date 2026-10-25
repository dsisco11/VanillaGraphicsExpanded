using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Owns the offline GLSL 330 fullscreen HDR galaxy executable.</summary>
[ShaderProgram("Contract", "celestial_galaxy", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "celestial_galaxy.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "celestial_galaxy.fsh")]
internal sealed partial class CelestialGalaxyProgram : GpuProgram, ICelestialDrawBindings
{
    internal readonly CelestialDrawInputs DrawInputs;
    private VgeFrameUniformBuffer? frame;
    private GpuShaderStorageBuffer? frameLightingInputs;
    #region Public API
    /// <summary>Registers complete view, source and storage bindings.</summary>
    public CelestialGalaxyProgram()
    {
        DrawInputs = OwnUniformBuffer(new CelestialDrawInputs());
        ProgramLayout.RegisterContract(Contract.Stages[0].Bindings);
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Identifies the precompiled map program.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    /// <summary>Supplies only generated map storage.</summary>
    public partial GpuTextureRef GalaxyMap { set; }
    /// <summary>Supplies cached existing atmospheric extinction.</summary>
    public partial GpuTextureRef ExternalTransmission { set; }
    /// <summary>Borrows a complete alternate view only outside prepared submissions.</summary>
    internal VgeFrameUniformBuffer? Frame { get => frame; set { RequireInputMutation(); frame = value; } }
    /// <summary>Borrows explicit alternate-view storage while normal world draws use frozen camera storage.</summary>
    internal GpuShaderStorageBuffer? FrameLightingInputs
    {
        get => frameLightingInputs;
        set { RequireInputMutation(); frameLightingInputs = value; }
    }
    /// <summary>Supplies exactly the frozen physical-light storage admitted for this view.</summary>
    GpuShaderStorageBuffer CameraExposure.IPhotometricFrameBindings.FrameLighting => frameLightingInputs ?? CameraExposure.CameraExposureRenderer.FrozenLighting;
    /// <summary>Returns the retained camera or fixture override.</summary>
    CpuUniformBuffer ICelestialDrawBindings.FrameInputs => Frame ?? VgeFrameRenderer.Current;
    /// <summary>Returns this executable's guarded packed inputs.</summary>
    CpuUniformBuffer ICelestialDrawBindings.Inputs => DrawInputs;
    #endregion
}
