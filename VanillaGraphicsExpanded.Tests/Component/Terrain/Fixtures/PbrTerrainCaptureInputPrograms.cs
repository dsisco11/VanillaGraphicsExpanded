using System;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Uniforms;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Terrain.Fixtures;

/// <summary>Shares the authored TestInputs block capture across the three terrain numerical fixture variants.</summary>
internal abstract class PbrTerrainCaptureInputProgram : GpuProgram
{
    private static readonly ShaderVariantResolver catalog = TestShaderPrograms.Create();
    private readonly GpuShaderContract contract;
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes;

    #region Public API
    /// <summary>Uses the immutable catalog identity chosen by the concrete fixture declaration.</summary>
    internal override GpuShaderContract ProgramContract => contract;
    #endregion

    #region Protected API
    /// <summary>Resolves one existing test catalog contract and retains its exact block shape.</summary>
    protected PbrTerrainCaptureInputProgram(string identity, int blockSize)
    {
        contract = catalog.Programs[identity];
        bytes = new byte[blockSize];
        inputs = OwnUniformBuffer(new PackedUniformBuffer(blockSize));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Captures each case into the existing std140 TestInputs block before executable submission.</summary>
    protected void CaptureInputs(Action<byte[]> write)
    {
        RequireInputMutation();
        Array.Clear(bytes, 0, bytes.Length);
        write(bytes);
        inputs.SetPayload(bytes);
    }

    /// <summary>Supplies one optional, already-prepared variant sampler with the retained input block.</summary>
    protected RHIResourceBindings CaptureInputBindings(params RHISamplerBinding[] samplers)
        => new(Shader, samplers: samplers, uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "TestInputs"), inputs.Capture())]);
    #endregion
}

/// <summary>Owns the normal correction fixture's exact scalar and integer controls.</summary>
internal sealed class TerrainNormalInputProgram : PbrTerrainCaptureInputProgram
{
    #region Public API
    /// <summary>Declares the existing 16-byte normal-input shader variant.</summary>
    public TerrainNormalInputProgram() : base("tests/normal-input", 16) { }

    /// <summary>Stages transmission, two-sidedness, smooth-normal and facing controls at their std140 offsets.</summary>
    internal void Capture(float transmission, bool twoSided, bool smooth, bool backFacing)
        => CaptureInputs(bytes =>
        {
            UboPacking.WriteFloat(bytes, 0, transmission);
            UboPacking.WriteInt32(bytes, 4, twoSided ? 1 : 0);
            UboPacking.WriteInt32(bytes, 8, smooth ? 1 : 0);
            UboPacking.WriteInt32(bytes, 12, backFacing ? 1 : 0);
        });
    #endregion

    #region Protected API
    /// <summary>Captures this variant's retained numeric block for the real raster pass.</summary>
    protected override RHIResourceBindings CaptureBindingsCore() => CaptureInputBindings();
    #endregion
}

/// <summary>Owns the foliage transmission fixture's numeric inputs and production energy lookup binding.</summary>
internal sealed class FoliageTransmissionInputProgram : PbrTerrainCaptureInputProgram
{
    private GpuTexture? energyLookup;
    private GpuSampler? energySampler;

    #region Public API
    /// <summary>Declares the existing 48-byte foliage transmission shader variant.</summary>
    public FoliageTransmissionInputProgram() : base("tests/foliage-transmission", 48) { }

    /// <summary>Supplies the already-prepared production lookup texture before input capture.</summary>
    internal GpuTexture? EnergyLookup
    {
        get => energyLookup;
        set { RequireInputMutation(); energyLookup = value; }
    }

    /// <summary>Supplies the already-prepared linear sampler used by the production lookup.</summary>
    internal GpuSampler? EnergySampler
    {
        get => energySampler;
        set { RequireInputMutation(); energySampler = value; }
    }

    /// <summary>Stages visibility and the exact light/view direction vectors for the numerical case.</summary>
    internal void Capture(float visibility, bool frontLit, bool sideView)
        => CaptureInputs(bytes =>
        {
            UboPacking.WriteFloat(bytes, 0, visibility);
            UboPacking.WriteVec3(bytes, 16, 0, 0, frontLit ? 1 : -1);
            UboPacking.WriteVec3(bytes, 32, sideView ? 1 : 0, 0, sideView ? 0 : 1);
        });
    #endregion

    #region Protected API
    /// <summary>Captures the numeric block and required shared microfacet sampler in the declared binding table.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var texture = EnergyLookup ?? throw new InvalidOperationException("Foliage fixture energy lookup is unset.");
        var sampler = EnergySampler ?? throw new InvalidOperationException("Foliage fixture energy sampler is unset.");
        return CaptureInputBindings(new RHISamplerBinding(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_microfacetEnergy"),
            texture, Sampler: sampler));
    }
    #endregion
}

/// <summary>Owns the liquid interface fixture's cosine and underwater controls.</summary>
internal sealed class LiquidInterfaceInputProgram : PbrTerrainCaptureInputProgram
{
    #region Public API
    /// <summary>Declares the existing liquid optics shader variant and 16-byte fixture block.</summary>
    public LiquidInterfaceInputProgram() : base("tests/liquid-interface", 16) { }

    /// <summary>Stages the authored interface cosine and std140 boolean value.</summary>
    internal void Capture(float cosine, bool underwater)
        => CaptureInputs(bytes =>
        {
            UboPacking.WriteFloat(bytes, 0, cosine);
            UboPacking.WriteInt32(bytes, 4, underwater ? 1 : 0);
        });
    #endregion

    #region Protected API
    /// <summary>Captures this variant's retained numeric block for the real raster pass.</summary>
    protected override RHIResourceBindings CaptureBindingsCore() => CaptureInputBindings();
    #endregion
}
