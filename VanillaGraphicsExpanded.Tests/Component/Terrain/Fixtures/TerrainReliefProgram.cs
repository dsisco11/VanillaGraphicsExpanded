using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Terrain.Fixtures;

/// <summary>Shares typed material and metric inputs between the packaged terrain-relief variants.</summary>
internal abstract class TerrainReliefProgram : GpuProgram
{
    private static readonly IReadOnlyDictionary<string, GpuShaderContract> contracts =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs;
    private readonly GpuShaderContract contract;
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[16];
    private VgeFrameUniformBuffer? frame;
    private GpuTexture? heightTexture, displacementIndices, displacementRecords;

    #region Public API
    /// <summary>Identifies the constructor-selected packaged relief executable.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Supplies the controlled frame used by the material includes.</summary>
    internal VgeFrameUniformBuffer? FrameInputs { set { RequireInputMutation(); frame = value; } }

    /// <summary>Supplies the packed height and normal texture.</summary>
    internal GpuTexture? HeightTexture { set { RequireInputMutation(); heightTexture = value; } }

    /// <summary>Supplies the material's displacement index texture.</summary>
    internal GpuTexture? DisplacementIndices { set { RequireInputMutation(); displacementIndices = value; } }

    /// <summary>Supplies the material's displacement amplitude and atlas records.</summary>
    internal GpuTexture? DisplacementRecords { set { RequireInputMutation(); displacementRecords = value; } }

    /// <summary>Stages the sidedness and world-space metric used by the POM include.</summary>
    internal void Capture(float scale, float distance)
    {
        RequireInputMutation();
        UboPacking.WriteInt32(bytes, 0, 0);
        UboPacking.WriteVec2(bytes, 8, scale, distance);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Registers the selected variant and retains its typed metric block.</summary>
    protected TerrainReliefProgram(bool enabled)
    {
        contract = contracts[enabled ? "tests/relief-on" : "tests/relief-off"];
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Publishes all active material samples and the captured camera and metric blocks.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        return new(Shader,
            samplers:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_normalDepthTex"), heightTexture),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_displacementTex"), displacementIndices),
                new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_displacementRecords"), displacementRecords)
            ],
            uniforms:
            [
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"), frame?.Capture()),
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "ReliefInputs"), inputs.Capture())
            ]);
    }
    #endregion
}

/// <summary>Selects the packaged relief-enabled executable for typed fixture construction.</summary>
internal sealed class TerrainReliefEnabledProgram : TerrainReliefProgram
{
    #region Public API
    /// <summary>Registers the relief-enabled shader contract before requested settings are captured.</summary>
    public TerrainReliefEnabledProgram() : base(true) { }
    #endregion
}

/// <summary>Selects the packaged relief-disabled executable for typed fixture construction.</summary>
internal sealed class TerrainReliefDisabledProgram : TerrainReliefProgram
{
    #region Public API
    /// <summary>Registers the relief-disabled shader contract before requested settings are captured.</summary>
    public TerrainReliefDisabledProgram() : base(false) { }
    #endregion
}
