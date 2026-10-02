using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns liquid SPIR-V and publishes mesh-pool parameters through the engine shader interface.</summary>
[ShaderProgram("Contract", "pbr_liquid", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_liquid.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_liquid.fsh")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(CaptureMode))]
internal sealed partial class LiquidShaderProgram : GpuProgram, IShaderProgram, ILiquidShaderProgramBindings
{



    /// <summary>Selects normal OIT output or a precompiled diagnostic output for GPU tests.</summary>
    [ShaderOption("VGE_LIQUID_CAPTURE_MODE", 0, Domain = new object[] { 0, 1, 2, 3 })]
    internal partial int CaptureMode { get; set; }
    private readonly LiquidDrawParamsUbo draw = new();
    private readonly LiquidFrameParamsUbo frame = new();
    private readonly LiquidWaveParamsUbo wave = new();
    /// <summary>Returns the generated offline contract.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    #region Binding
    /// <summary>Installs the same resource contract used by the offline compiler.</summary>
    protected override GpuProgramLayout CreateLayout()
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(GpuShaderContracts.Create("pbr_liquid"));
        return layout;
    }




    #endregion
    /// <summary>Attaches mutation guards to all retained blocks.</summary>
    public LiquidShaderProgram()
    {
        frame.SetWriteGuard(RequireInputMutation);
        draw.SetWriteGuard(RequireInputMutation);
        wave.SetWriteGuard(RequireInputMutation);
    }
    /// <summary>Supplies retained frame inputs.</summary>
    CpuUniformBuffer ILiquidShaderProgramBindings.FrameParameters => frame;
    /// <summary>Supplies retained draw inputs.</summary>
    CpuUniformBuffer ILiquidShaderProgramBindings.DrawParameters => draw;
    /// <summary>Supplies retained wave inputs.</summary>
    CpuUniformBuffer ILiquidShaderProgramBindings.WaveParameters => wave;
}
