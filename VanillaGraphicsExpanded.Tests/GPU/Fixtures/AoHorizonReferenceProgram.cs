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
    protected override void Submit()
    {
        var frame = ShaderPreparedSubmission.Resolve(this, GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"));
        var parameters = ShaderPreparedSubmission.Resolve(this, GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "AmbientOcclusionInputs"));
        var depthBinding = ShaderPreparedSubmission.Resolve(this, GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "depthImage"));
        var surfaceBinding = ShaderPreparedSubmission.Resolve(this, GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "surfaceImage"));
        var hierarchyBinding = ShaderPreparedSubmission.Resolve(this, GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "depthHierarchy"));
        var sharedCamera = camera ?? throw new InvalidOperationException("Reference AO requires a shared camera.");
        ShaderPreparedSubmission.ValidateUniformBlock(frame, sharedCamera);
        ShaderPreparedSubmission.ValidateUniformBlock(parameters, inputs);
        ShaderPreparedSubmission.ValidateSampler(depthBinding, depth);
        ShaderPreparedSubmission.ValidateSampler(surfaceBinding, surface);
        ShaderPreparedSubmission.ValidateSampler(hierarchyBinding, hierarchy);
        ShaderPreparedSubmission.UniformBlock(frame, sharedCamera);
        ShaderPreparedSubmission.UniformBlock(parameters, inputs);
        ShaderPreparedSubmission.Sampler(depthBinding, depth);
        ShaderPreparedSubmission.Sampler(surfaceBinding, surface);
        ShaderPreparedSubmission.Sampler(hierarchyBinding, hierarchy);
    }
    #endregion
}
