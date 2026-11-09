using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Owns typed inputs for observing production scene-storage containment at a grazing highlight.</summary>
internal sealed class FrameLightingGrazingProbeProgram : GpuProgram
{
    private static readonly GpuShaderContract contract =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/frame-lighting-grazing"];
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[16];
    private GpuShaderStorageBuffer? lighting;
    private GpuTexture? energyLookup;
    private GpuSampler? energySampler;

    #region Public API
    /// <summary>Registers the declared control block used by the production scene-storage observation.</summary>
    public FrameLightingGrazingProbeProgram()
    {
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Identifies the packaged grazing-highlight probe executable.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Captures the highlight selector and borrows the production frame storage and lookup resources.</summary>
    internal void Capture(int highlight, GpuShaderStorageBuffer frameLighting,
        GpuTexture lookup, GpuSampler sampler)
    {
        RequireInputMutation();
        lighting = frameLighting;
        energyLookup = lookup;
        energySampler = sampler;
        UboPacking.WriteInt32(bytes, 0, highlight);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Publishes typed frame, lookup, sampler, and control resources for the prepared draw.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var frame = lighting ?? throw new InvalidOperationException("Frame lighting has not been captured.");
        var lookup = energyLookup ?? throw new InvalidOperationException("Microfacet lookup has not been captured.");
        var sampler = energySampler ?? throw new InvalidOperationException("Microfacet sampler has not been captured.");
        return new(Shader,
            buffers: [new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "FrozenFrameLighting"),
                BufferRole.ShaderStorage, new GpuBufferRef(frame))],
            samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_microfacetEnergy"),
                lookup, Sampler: sampler)],
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "GrazingProbeInputs"),
                inputs.Capture())]);
    }
    #endregion
}
