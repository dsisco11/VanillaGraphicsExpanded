using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the shared authored microfacet cost kernel and its baseline/compensated executable variants.</summary>
internal sealed class MicrofacetEnergyMeasurementProgram : GpuProgram
{
    private static readonly GpuShaderContract singleContract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/microfacet-measurement-single"];
    private static readonly GpuShaderContract compensatedContract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/microfacet-measurement-compensated"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[16];
    private bool compensated;
    private GpuTexture? energyLookup;
    private GpuSampler? energySampler;

    #region Public API
    /// <summary>Registers the numeric block used by both fixed kernel variants.</summary>
    public MicrofacetEnergyMeasurementProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
    }

    /// <summary>Selects the fixed baseline or compensated shader contract before initialization.</summary>
    internal bool Compensated
    {
        get => compensated;
        set
        {
            if (!Shader.IsEmpty) throw new InvalidOperationException("The measurement variant is fixed at preparation time.");
            compensated = value;
            foreach (var stage in ProgramContract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
        }
    }

    /// <summary>Identifies one fixed variant of the shared authored measurement kernel.</summary>
    internal override GpuShaderContract ProgramContract => compensated ? compensatedContract : singleContract;

    /// <summary>Captures repetition count and the lookup resources used by the compensated variant.</summary>
    internal void Capture(int evaluations, GpuTexture lookup, GpuSampler sampler)
    {
        RequireInputMutation();
        energyLookup = lookup;
        energySampler = sampler;
        UboPacking.WriteInt32(bytes, 0, evaluations);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the typed workload controls and only the sampler consumed by the selected variant.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        if (!compensated) return new(Shader,
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "MicrofacetMeasurementInputs"), inputs.Capture())]);
        var lookup = energyLookup ?? throw new InvalidOperationException("Microfacet lookup has not been captured.");
        var sampler = energySampler ?? throw new InvalidOperationException("Microfacet sampler has not been captured.");
        return new(Shader, samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,
            "vge_microfacetEnergy"), lookup, Sampler: sampler)],
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,
                "MicrofacetMeasurementInputs"), inputs.Capture())]);
    }
    #endregion
}
