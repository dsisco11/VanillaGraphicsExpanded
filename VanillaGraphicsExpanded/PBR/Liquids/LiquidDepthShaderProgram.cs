using System;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns precompiled liquid-depth shaders and the engine pool's logical draw inputs.</summary>
[ShaderProgram("Contract", "pbr_liquid_depth", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_liquid_depth.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "pbr_liquid_depth.fsh")]
internal sealed partial class LiquidDepthShaderProgram : GpuProgram, IShaderProgram
{
    private readonly LiquidDepthFrameParamsUbo frame = new();
    private readonly LiquidWaveParamsUbo wave = new();
    private readonly LiquidDrawParamsUbo draw = new();

    /// <summary>Returns the generated offline shader contract.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    #region Public API
    /// <summary>Stages the same projection used by the visible liquid pass.</summary>
    internal ReadOnlySpan<float> ProjectionMatrix { set => frame.ProjectionMatrix = value; }

    /// <summary>Stages the shared wave snapshot before the depth draw.</summary>
    internal LiquidWaveFrame WaveFrame { set { wave.Phases = value.Phases; wave.Wind = value.Wind; } }

    /// <summary>Stages and publishes the engine's pool transform.</summary>
    internal float[] ModelViewMatrix { set { draw.SetModelView(value); PublishDraw(); } }

    /// <summary>Stages and publishes the engine's pool origin.</summary>
    internal Vector3 Origin { set { draw.SetOrigin(value); PublishDraw(); } }

    /// <summary>Publishes frame and wave snapshots before any mesh-pool submission.</summary>
    internal void ApplyInputs()
    {
        if (!GpuUniformRingSystem.TryBind(this, LiquidDepthFrameParamsUbo.BlockName, frame.Bytes, "VGE.LiquidDepth.Frame", true)
            || !GpuUniformRingSystem.TryBind(this, LiquidWaveParamsUbo.BlockName, wave.Bytes, "VGE.LiquidDepth.Waves", true))
            throw new InvalidOperationException("Liquid depth requires an active uniform ring and linked frame blocks.");
        PublishDraw();
    }
    #endregion

    #region Private
    /// <summary>Registers the generated explicit shader layout.</summary>
    protected override GpuProgramLayout CreateLayout()
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(GpuShaderContracts.Create("pbr_liquid_depth"));
        return layout;
    }

    /// <summary>Publishes one immutable transform/origin snapshot for the following pool draw.</summary>
    private void PublishDraw()
    {
        if (!GpuUniformRingSystem.TryBind(this, LiquidDrawParamsUbo.BlockName, draw.Bytes, "VGE.LiquidDepth.Draw", true))
            throw new InvalidOperationException("Liquid depth requires an active uniform ring and linked draw block.");
    }

    /// <summary>Advertises pool inputs whose backing storage is a UBO.</summary>
    bool IShaderProgram.HasUniform(string name)
        => name is "origin" or "modelViewMatrix" or "forcedTransparency" || HasUniform(name);

    /// <summary>Publishes a pool-origin write through the shared draw block.</summary>
    void IShaderProgram.Uniform(string name, Vec3f value)
    {
        if (name != "origin") { Uniform(name, value); return; }
        Origin = new(value.X, value.Y, value.Z);
    }

    /// <summary>Publishes mini-dimension transforms and their restoration writes.</summary>
    void IShaderProgram.UniformMatrix(string name, float[] value)
    {
        if (name != "modelViewMatrix") { UniformMatrix(name, value); return; }
        ModelViewMatrix = value;
    }

    /// <summary>Retains preview transparency in the pool's shared draw block.</summary>
    void IShaderProgram.Uniform(string name, float value)
    {
        if (name != "forcedTransparency") { Uniform(name, value); return; }
        draw.SetTransparency(value);
        PublishDraw();
    }
    #endregion
}
