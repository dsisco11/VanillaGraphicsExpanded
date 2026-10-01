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
internal sealed partial class LiquidDepthShaderProgram : GpuProgram, IShaderProgram, ILiquidDepthShaderProgramBindings
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

    /// <summary>Stages the engine's pool transform.</summary>
    internal float[] ModelViewMatrix { set { draw.SetModelView(value); } }

    /// <summary>Stages the engine's pool origin.</summary>
    internal Vector3 Origin { set { draw.SetOrigin(value); } }

    #endregion

    #region Private
    /// <summary>Registers the generated explicit shader layout.</summary>
    protected override GpuProgramLayout CreateLayout()
    {
        var layout = new GpuProgramLayout();
        layout.RegisterContract(GpuShaderContracts.Create("pbr_liquid_depth"));
        return layout;
    }


    /// <summary>Advertises pool inputs whose backing storage is a UBO.</summary>
    bool IShaderProgram.HasUniform(string name)
        => name is "origin" or "modelViewMatrix" or "forcedTransparency" || HasUniform(name);

    /// <summary>Retains a pool-origin write through the shared draw block.</summary>
    void IShaderProgram.Uniform(string name, Vec3f value)
    {
        if (name != "origin") { Uniform(name, value); return; }
        Origin = new(value.X, value.Y, value.Z);
    }

    /// <summary>Retains mini-dimension transforms and their restoration writes.</summary>
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

    }
    #endregion
    /// <summary>Attaches mutation guards to all retained blocks.</summary>
    public LiquidDepthShaderProgram()
    {
        frame.SetWriteGuard(RequireInputMutation);
        draw.SetWriteGuard(RequireInputMutation);
        wave.SetWriteGuard(RequireInputMutation);
    }
    /// <summary>Supplies retained frame inputs.</summary>
    CpuUniformBuffer ILiquidDepthShaderProgramBindings.FrameParameters => frame;
    /// <summary>Supplies retained draw inputs.</summary>
    CpuUniformBuffer ILiquidDepthShaderProgramBindings.DrawParameters => draw;
    /// <summary>Supplies retained wave inputs.</summary>
    CpuUniformBuffer ILiquidDepthShaderProgramBindings.WaveParameters => wave;
}
