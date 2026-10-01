using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns liquid SPIR-V and publishes mesh-pool parameters through the engine shader interface.</summary>
[ShaderProgram("Contract", "pbr_liquid", 3)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_liquid.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_liquid.fsh")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(CaptureMode))]
internal sealed partial class LiquidShaderProgram : GpuProgram, IShaderProgram, ILiquidShaderProgramBindings
{

    /// <summary>Selects normal OIT output or a precompiled diagnostic output for GPU tests.</summary>
    [ShaderOption("VGE_LIQUID_CAPTURE_MODE", 0, Domain = new object[] { 0, 1, 2 })]
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

    /// <summary>Publishes an immutable draw snapshot before the engine's following mesh submission.</summary>
    private void PublishDraw()
    {
        if (!GpuUniformRingSystem.TryBind(this, LiquidDrawParamsUbo.BlockName, draw.Bytes, "VGE.Liquid.Draw", true))
            throw new InvalidOperationException("Liquid draw parameters require an active uniform ring and linked block.");
    }

    /// <summary>Publishes frame inputs once after all CPU-side fields have been captured.</summary>
    private void PublishFrame()
    {
        if (!GpuUniformRingSystem.TryBind(this, LiquidFrameParamsUbo.BlockName, frame.Bytes, "VGE.Liquid.Frame", true))
            throw new InvalidOperationException("Liquid frame parameters require an active uniform ring and linked block.");
    }

    /// <summary>Binds an externally owned image using the program's declared resource slot.</summary>
    private void BindImage(string name, int texture, TextureTarget target, int sampler)
        => ProgramLayout.TryBindSamplerTextureActive(ProgramId, name, target, texture, sampler, LayoutWarn);
    #endregion
}
