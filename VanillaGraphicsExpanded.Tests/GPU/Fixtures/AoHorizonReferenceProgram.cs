using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Publishes production inputs to the frozen horizon shader for matched image and cost comparisons.</summary>
internal sealed class AoHorizonReferenceProgram : GpuProgram
{
    private static readonly GpuShaderContract contract = ShaderBuildTool.Spirv.TestShaderPrograms.Create().Programs["tests/ao-horizon-reference"];
    private readonly AmbientOcclusionInputs inputs;
    private VgeFrameUniformBuffer? camera;
    private GpuTexture? depth, surface, hierarchy;

    #region Public API
    /// <summary>Exposes borrowed receiver depth for equivalent-work complete-owner comparisons.</summary>
    internal GpuTexture ReceiverDepth => depth ?? throw new InvalidOperationException("AO input depth is missing.");
    /// <summary>Exposes the borrowed hierarchy for exact mip comparisons before measurement.</summary>
    internal GpuTexture Hierarchy => hierarchy ?? throw new InvalidOperationException("AO hierarchy is missing.");
    /// <summary>Exposes unchanged borrowed material layers for complete-owner comparisons.</summary>
    internal GpuTexture Surface => surface ?? throw new InvalidOperationException("AO surface is missing.");
    /// <summary>Exposes the shared view snapshot without changing its lifetime.</summary>
    internal VgeFrameUniformBuffer Camera => camera ?? throw new InvalidOperationException("AO camera is missing.");
    /// <summary>Registers the unchanged production resource contract and owns retained sampling parameters.</summary>
    public AoHorizonReferenceProgram()
    {
        inputs = OwnUniformBuffer(new AmbientOcclusionInputs());
        foreach (var stage in contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
    }

    /// <summary>Selects the test-only binary independently of the current production horizon source.</summary>
    internal override GpuShaderContract ProgramContract => contract;

    /// <summary>Borrows identical scene inputs and stages the existing native quality policy.</summary>
    internal void Capture(GpuTexture depth, GpuTexture surface, GpuTexture hierarchy, VgeFrameUniformBuffer camera, int nativeQuality)
    {
        RequireInputMutation();
        this.depth = depth; this.surface = surface; this.hierarchy = hierarchy; this.camera = camera;
        var quality = AmbientOcclusionQuality.FromNative(nativeQuality);
        inputs.Capture(new(0, 0, quality.Divisor, 0), new(1.25f, .25f, quality.Directions, quality.Steps), new(64, 96, 0, 0));
    }
    #endregion

    #region Protected API
    /// <summary>Validates all descriptors before publishing the shared textures and immutable uniform slices.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        return new(Shader,samplers:[new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"depthImage"),depth),
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"surfaceImage"),surface),
            new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler,"depthHierarchy"),hierarchy)],
            uniforms:[new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"VgeFrameUBO"),camera?.Capture()),
                new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock,"AmbientOcclusionInputs"),inputs.Capture())]);
    }
    #endregion
}
