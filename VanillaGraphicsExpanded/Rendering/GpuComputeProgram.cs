using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns retained compute inputs and publishes them before executing work.</summary>
internal abstract partial class GpuComputeProgram : IGpuProgram
{
    protected ComputeShaderAsset pipeline;
    private readonly Func<ComputeShaderAsset?>? rebuild;
    private readonly Func<string>? rebuildDiagnostic;
    private readonly Integration.ClientRenderingLifetime? renderingLifetime;
    private readonly IDisposable? reloadRegistration;
    private bool reloadRequired, reloadFailed;
    private string? reloadFailure;
    private ulong executableRevision, assetGeneration;
    private readonly GpuProgramLifetime lifetime = new();

    /// <summary>Gets the exact installed executable for immutable capture, independently of pending asset settings.</summary>
    internal RHIShaderRef Shader => pipeline.Shader;
    /// <summary>Gets the exact installed compute realization for explicit boundary admission.</summary>
    internal RHIComputePipelineRef Pipeline => pipeline.Pipeline;

    #region Public API
    /// <summary>Freezes retained compute inputs without native preparation, metadata access or physical uniform allocation.</summary>
    public RHIResourceBindings CaptureBindings()
    {
        lifetime.RequireMutation(); lifetime.RequireSubmissionAdmission();
        if (Shader.IsEmpty) throw new InvalidOperationException("Prepare a compute executable before capturing bindings.");
        lifetime.IsPublishing = true;
        try { return CaptureBindingsCore(); }
        finally { lifetime.IsPublishing = false; }
    }
    #region Executable state
    /// <summary>Gets the current executable identifier.</summary>
    public int ProgramId => pipeline.ProgramId;
    /// <summary>Gets the installed binding layout.</summary>
    public GpuProgramLayout ProgramLayout => pipeline.ProgramLayout;
    /// <summary>Reports whether the owned executable remains available.</summary>
    public bool IsValid => !lifetime.IsRetired && !reloadRequired && pipeline.IsValid && !pipeline.NeedsNativePreparation;
    /// <summary>Reports the owned executable's last preparation failure.</summary>
    public string PreparationLog => reloadRequired ? reloadFailure ?? rebuildDiagnostic?.Invoke() ?? pipeline.PreparationLog : pipeline.PreparationLog;
    /// <summary>Prepares the executable without publishing inputs or executing work.</summary>
    public bool EnsureReady() => ((IGpuProgram)this).Prepare();
    #endregion

    #region Submission and execution
    /// <summary>Publishes current inputs immediately before dispatching the requested work.</summary>
    public virtual void Dispatch(int x, int y = 1, int z = 1)
    {
        RequireInputMutation();
        if (!EnsureReady()) throw new InvalidOperationException("Compute executable preparation failed.");
        var context = RHICommandContext.Current;
        context.SetPipeline(pipeline.Pipeline);
        context.BindResources(CaptureBindings());
        context.Dispatch(new(x, y, z));
    }

    /// <summary>Publishes retained inputs before executing previously produced indirect arguments.</summary>
    public void DispatchIndirect(GpuIndirectBuffer arguments, nint offset = 0)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!arguments.IsValid) throw new ObjectDisposedException(nameof(arguments));
        RequireInputMutation();
        if (!EnsureReady()) throw new InvalidOperationException("Compute executable preparation failed.");
        var context = RHICommandContext.Current;
        context.SetPipeline(pipeline.Pipeline);
        context.BindResources(CaptureBindings());
        context.DispatchIndirect(new RHIBufferRange(arguments, offset, 12));
    }
    #endregion

    #region Lifetime and restoration
    /// <summary>Releases the executable after rejecting recursive disposal during publication.</summary>
    public void Dispose() => ((IGpuProgram)this).Retire();
    #endregion
    #endregion

    #region Protected API
    /// <summary>Adopts the existing compute executable and its preparation policy.</summary>
    protected GpuComputeProgram(ComputeShaderAsset pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        lifetime.NativeExecutable = pipeline.NativeProgram;
        rebuild = pipeline.Rebuild;
        rebuildDiagnostic = pipeline.RebuildDiagnostic;
        renderingLifetime = pipeline.RenderingLifetime;
        lifetime.SubmissionAdmission = () => renderingLifetime is not ({ IsClosed: true } or { Reloading: true });
        executableRevision = pipeline.ExecutableRevision;
        if (rebuild is not null) reloadRegistration = renderingLifetime?.RegisterCompute(RequireReloadBoundary, InvalidateAssets);
    }
    /// <summary>Allows derived resource owners to preserve idempotent disposal.</summary>
    protected bool IsDisposed => lifetime.IsRetired;
    /// <summary>Rejects input writes during submission or after disposal.</summary>
    protected void RequireInputMutation() => lifetime.RequireMutation();
    /// <summary>Registers CPU blocks owned by the compute wrapper rather than its pipeline.</summary>
    protected T OwnUniformBuffer<T>(T buffer) where T : CpuUniformBuffer
        => ((IGpuProgram)this).RegisterUniform(buffer);
    /// <summary>Releases feature-owned resources after terminal admission closes.</summary>
    protected virtual void ReleaseResources() { }
    /// <summary>Implemented by generated binding-contract publication.</summary>
    protected abstract RHIResourceBindings CaptureBindingsCore();
    #endregion

    #region Private
    #region Workflow hooks
    /// <summary>Identifies the installed compute executable generation.</summary>
    ulong IGpuProgram.ExecutableRevision => executableRevision;
    /// <summary>Reports whether current compute assets require initial preparation or executable replacement.</summary>
    bool IGpuProgram.RequiresPreparation => reloadRequired || !pipeline.IsValid || pipeline.NeedsNativePreparation;
    /// <summary>Identifies the compute executable's originating context.</summary>
    (nint Handle, long Generation) IGpuProgram.ExecutableContext => pipeline.ExecutableContext;
    /// <summary>Exposes stable lifecycle data solely to the common workflow.</summary>
    GpuProgramLifetime IGpuProgram.Lifetime => lifetime;


    /// <summary>Routes interface disposal through the same terminal facade.</summary>
    void IDisposable.Dispose() => Dispose();
    /// <summary>Releases the single compute executable owner after all activation borrows end.</summary>
    void IGpuProgram.ReleaseExecutable()
    {
        reloadRegistration?.Dispose();
        pipeline.Dispose();
    }
    /// <summary>Invokes extra-resource cleanup independently of native executable cleanup.</summary>
    void IGpuProgram.ReleaseResources() => ReleaseResources();

    #endregion

    #endregion
}
