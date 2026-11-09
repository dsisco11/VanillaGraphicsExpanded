using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Owns the declared microfacet probe shader, its exact numeric block, and production lookup binding.</summary>
internal sealed class MicrofacetEnergyProbeProgram : GpuProgram
{
    private static readonly GpuShaderContract contract = TestShaderPrograms.Create().Programs["tests/microfacet-energy"];

    #region Public API
    /// <summary>Declares the packaged microfacet probe and its fixed 32-byte input block.</summary>
    public MicrofacetEnergyProbeProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Identifies the declared microfacet energy diagnostic executable.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Supplies the production lookup texture before input capture.</summary>
    internal GpuTexture? EnergyLookup
    {
        set { RequireInputMutation(); energyLookup = value; }
    }

    /// <summary>Supplies the production linear clamp sampler before input capture.</summary>
    internal GpuSampler? EnergySampler
    {
        set { RequireInputMutation(); energySampler = value; }
    }

    /// <summary>Captures a batched RG32F coordinate image for direct production lookup observations.</summary>
    internal void CaptureLookupBatch(GpuTexture coordinates, GpuSampler sampler)
    {
        RequireInputMutation();
        coordinateImage = coordinates;
        coordinateSampler = sampler;
        Array.Clear(bytes);
        UboPacking.WriteInt32(bytes, 28, 1);
        inputs.SetBytes(bytes);
    }

    /// <summary>Captures the seven scalar controls in the shader block's declared order and std140 offsets.</summary>
    internal void Capture(float roughness, float viewCosine, float material, int furnace,
        float environmentMetallic, int grazing, float waterAngle)
    {
        RequireInputMutation();
        coordinateImage = null;
        coordinateSampler = null;
        UboPacking.WriteFloat(bytes, 0, roughness);
        UboPacking.WriteFloat(bytes, 4, viewCosine);
        UboPacking.WriteFloat(bytes, 8, material);
        UboPacking.WriteInt32(bytes, 12, furnace);
        UboPacking.WriteFloat(bytes, 16, environmentMetallic);
        UboPacking.WriteInt32(bytes, 20, grazing);
        UboPacking.WriteFloat(bytes, 24, waterAngle);
        UboPacking.WriteInt32(bytes, 28, 0);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the exact numeric block and required production lookup texture/sampler pair.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var lookup = energyLookup ?? throw new InvalidOperationException("Microfacet energy lookup has not been supplied.");
        var sampler = energySampler ?? throw new InvalidOperationException("Microfacet energy sampler has not been supplied.");
        if (coordinateImage is { } coordinates)
        {
            var coordinateFilter = coordinateSampler ?? throw new InvalidOperationException("Lookup coordinate sampler has not been supplied.");
            return new(Shader,
                samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_microfacetEnergy"), lookup, Sampler: sampler),
                    new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "lookupCoordinates"), coordinates, Sampler: coordinateFilter)],
                uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,
                    "MicrofacetEnergyInputs"), inputs.Capture())]);
        }
        return new(Shader,
            samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_microfacetEnergy"), lookup, Sampler: sampler)],
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,
                "MicrofacetEnergyInputs"), inputs.Capture())]);
    }
    #endregion

    #region Private
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[32];
    private GpuTexture? energyLookup;
    private GpuSampler? energySampler;
    private GpuTexture? coordinateImage;
    private GpuSampler? coordinateSampler;
    #endregion
}
