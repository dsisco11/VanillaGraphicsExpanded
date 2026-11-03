using ShaderBuildTool.Spirv;
using Vintagestory.API.Common;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the packaged temporal reprojection compute executable and its typed output binding.</summary>
internal sealed class TemporalDebugComputeProgram : GpuComputeProgram
{
    private static readonly GpuShaderContract contract = TestShaderPrograms.Create().Programs["tests/temporal-debug"];

    #region Public API
    /// <summary>Creates the declared compute owner from the component test asset boundary.</summary>
    internal static TemporalDebugComputeProgram Create(ICoreAPI api, GpuShaderStorageBuffer output)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(output);
        var layout = new GpuProgramLayout();
        layout.RegisterContract(contract.Stages[0].Bindings);
        if (!ComputeShaderAsset.TryCreateFromAssets(api, new ShaderSettings(contract), out var pipeline,
            out string log, debugName: "Tests.LumOnTemporalDebug", layout: layout))
            throw new InvalidOperationException("Temporal debug compute shader unavailable: " + log);
        return new(pipeline!, output);
    }

    /// <summary>Supplies the captured previous-frame projection used by the authored compute helper.</summary>
    internal VgeFrameUniformBuffer? FrameInputs
    {
        get => frameInputs;
        set { RequireInputMutation(); frameInputs = value; }
    }
    #endregion

    #region Protected API
    /// <summary>Publishes the exact frame snapshot and result storage allocation declared by the compute contract.</summary>
    protected override RHIResourceBindings CaptureBindingsCore()
    {
        var frame = FrameInputs ?? throw new InvalidOperationException("Temporal debug frame inputs have not been captured.");
        return new(Shader,
            buffers: [new(GpuBindingEntry.Identity(ShaderBindingKind.StorageBlock, "Result"), BufferRole.ShaderStorage, output)],
            uniforms: [new(GpuBindingEntry.Identity(ShaderBindingKind.UniformBlock, "VgeFrameUBO"), frame.Capture())]);
    }
    #endregion

    #region Private
    private readonly GpuShaderStorageBuffer output;
    private VgeFrameUniformBuffer? frameInputs;

    /// <summary>Retains the exact prepared executable and borrows the caller-owned result buffer through dispatch.</summary>
    private TemporalDebugComputeProgram(ComputeShaderAsset pipeline, GpuShaderStorageBuffer output) : base(pipeline)
        => this.output = output;
    #endregion
}
