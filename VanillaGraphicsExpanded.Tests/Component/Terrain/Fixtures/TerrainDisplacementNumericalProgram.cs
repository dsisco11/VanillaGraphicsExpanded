using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.Terrain.Fixtures;

/// <summary>Shares typed displacement inputs across the metric, height and pre-camera shadow fixtures.</summary>
internal abstract class TerrainDisplacementNumericalProgram : GpuProgram
{
    private static readonly IReadOnlyDictionary<string, GpuShaderContract> contracts =
        ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs;
    private static readonly float[] identityMatrix = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];
    private readonly GpuShaderContract contract;
    private readonly bool usesFrame;
    private readonly PackedUniformBuffer inputs;
    private readonly byte[] bytes = new byte[176];
    private VgeFrameUniformBuffer? frame;
    private GpuTexture? normalDepthAtlas;

    #region Public API
    /// <summary>Identifies the constructor-selected numerical displacement executable.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Supplies the world camera for visible metric and height calculations.</summary>
    internal VgeFrameUniformBuffer? FrameInputs { set { RequireInputMutation(); frame = value; } }

    /// <summary>Supplies the packed normal and height atlas used by the authored height case.</summary>
    internal GpuTexture? NormalDepthAtlas { set { RequireInputMutation(); normalDepthAtlas = value; } }

    /// <summary>Stages focal metric and distance fade values for the camera-distance oracle.</summary>
    internal void CaptureMetric(float distance)
    {
        RequireInputMutation();
        UboPacking.WriteFloat(bytes, 172, distance);
        UboPacking.WriteFloat(bytes, 128, 1024f);
        UboPacking.WriteVec2(bytes, 136, 8f, 8f);
        UboPacking.WriteVec2(bytes, 144, 8f, 24f);
        inputs.SetBytes(bytes);
    }

    /// <summary>Stages sampled UV, signed amplitude and metric values for bounded height mapping.</summary>
    internal void CaptureHeight(float u, float v, float amplitude)
    {
        RequireInputMutation();
        MemoryMarshal.AsBytes(identityMatrix.AsSpan()).CopyTo(bytes.AsSpan(64));
        UboPacking.WriteVec3(bytes, 160, u, v, amplitude);
        UboPacking.WriteVec2(bytes, 144, 10f, 20f);
        UboPacking.WriteVec2(bytes, 136, 16f, 8f);
        inputs.SetBytes(bytes);
    }

    /// <summary>Stages focal metric and fade bounds without introducing a world-camera block.</summary>
    internal void CaptureShadow(float focalPixels)
    {
        RequireInputMutation();
        UboPacking.WriteFloat(bytes, 128, focalPixels);
        UboPacking.WriteVec2(bytes, 136, 8f, 8f);
        UboPacking.WriteVec2(bytes, 144, 8f, 24f);
        inputs.SetBytes(bytes);
    }
    #endregion

    #region Protected API
    /// <summary>Registers one immutable catalog program and its exact shared 176-byte block.</summary>
    protected TerrainDisplacementNumericalProgram(string identity, bool usesFrame)
    {
        contract = contracts[identity];
        this.usesFrame = usesFrame;
        inputs = OwnUniformBuffer(new PackedUniformBuffer(bytes.Length));
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Publishes only resources declared by the selected executable and typed fixture inputs.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        IEnumerable<RHIUniformBinding> uniforms = usesFrame
            ? [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"), frame?.Capture()),
               new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "DisplacementInputs"), inputs.Capture())]
            : [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "DisplacementInputs"), inputs.Capture())];
        IEnumerable<RHISamplerBinding>? samplers = normalDepthAtlas is null
            ? null
            : [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "vge_normalDepthTex"), normalDepthAtlas)];
        return new(Shader, samplers: samplers, uniforms: uniforms);
    }
    #endregion
}

/// <summary>Selects the existing production-metric fixture contract.</summary>
internal sealed class TerrainDisplacementMetricProgram : TerrainDisplacementNumericalProgram
{
    #region Public API
    /// <summary>Registers metric inputs with their shared frame block.</summary>
    public TerrainDisplacementMetricProgram() : base("tests/displacement-metric", usesFrame: true) { }
    #endregion
}

/// <summary>Selects the existing production-height fixture contract.</summary>
internal sealed class TerrainDisplacementHeightProgram : TerrainDisplacementNumericalProgram
{
    #region Public API
    /// <summary>Registers height inputs with their shared frame and atlas contracts.</summary>
    public TerrainDisplacementHeightProgram() : base("tests/displacement-height", usesFrame: true) { }
    #endregion
}

/// <summary>Selects the isolated shadow contract that has no world-frame uniform block.</summary>
internal sealed class TerrainDisplacementShadowProgram : TerrainDisplacementNumericalProgram
{
    #region Public API
    /// <summary>Registers only the pre-camera shadow displacement block.</summary>
    public TerrainDisplacementShadowProgram() : base("tests/displacement-shadow", usesFrame: false) { }
    #endregion
}
