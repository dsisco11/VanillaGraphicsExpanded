using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns retained compute inputs and publishes them before executing work.</summary>
internal abstract class GpuComputeProgram : IGpuProgram
{
    protected readonly GpuComputePipeline pipeline;
    private readonly GpuProgramLifetime lifetime = new();

    #region Public API
    #region Executable state
    /// <summary>Gets the current executable identifier.</summary>
    public int ProgramId => pipeline.ProgramId;
    /// <summary>Gets the installed binding layout.</summary>
    public GpuProgramLayout ProgramLayout => pipeline.ProgramLayout;
    /// <summary>Reports whether the owned executable remains available.</summary>
    public bool IsValid => !lifetime.IsRetired && pipeline.IsValid;
    /// <summary>Reports the owned executable's last preparation failure.</summary>
    public string PreparationLog => pipeline.PreparationLog;
    /// <summary>Prepares the executable without publishing inputs or executing work.</summary>
    public bool EnsureReady() => ((IGpuProgram)this).Prepare();
    #endregion

    #region Submission and execution
    /// <summary>Activates the executable and publishes the complete retained input set.</summary>
    public void Use() => ((IGpuProgram)this).Activate();

    /// <summary>Ends only this owner's current direct activation.</summary>
    public void Stop() => StateCache.Current.StopProgram(this);

    /// <summary>Publishes inputs and restores the previous executable on scope exit.</summary>
    public IDisposable UseScope() => ((IGpuProgram)this).BeginUse();

    /// <summary>Publishes current inputs immediately before dispatching the requested work.</summary>
    public virtual void Dispatch(int x, int y = 1, int z = 1)
    {
        using var scope = UseScope();
        GL.DispatchCompute(x, y, z);
    }

    /// <summary>Publishes retained inputs before executing previously produced indirect arguments.</summary>
    public void DispatchIndirect(GpuIndirectBuffer arguments, nint offset = 0)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (!arguments.IsValid) throw new ObjectDisposedException(nameof(arguments));
        using var scope = UseScope();
        using var binding = arguments.BindDispatchScope();
        GL.DispatchComputeIndirect(offset);
    }
    #endregion

    #region Lifetime and restoration
    /// <summary>Releases the executable after rejecting recursive disposal during publication.</summary>
    public void Dispose() => ((IGpuProgram)this).Retire();
    #endregion
    #endregion

    #region Protected API
    /// <summary>Adopts the existing compute executable and its preparation policy.</summary>
    protected GpuComputeProgram(GpuComputePipeline pipeline)
    {
        this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        lifetime.NativeExecutable = pipeline;
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
    protected abstract void Submit();
    #endregion

    #region Private
    #region Workflow hooks
    /// <summary>Preserves compute's eager or deferred one-time preparation policy.</summary>
    bool IGpuProgram.PrepareExecutable() => pipeline.EnsureReady();

    /// <summary>Admits compute ownership without consulting engine shader state.</summary>
    void IGpuProgram.BindExecutable() => StateCache.Current.ActivateProgram(this);
    /// <summary>Publishes the complete authored or generated input set.</summary>
    void IGpuProgram.PublishInputs() => Submit();
    /// <summary>Clears only the failing admitted compute activation.</summary>
    void IGpuProgram.ClearActivation(bool bindingEntered)
    {
        if (bindingEntered) StateCache.Current.StopProgram(this);
    }
    /// <summary>Identifies the installed compute executable generation.</summary>
    ulong IGpuProgram.ExecutableRevision => pipeline.ExecutableRevision;
    /// <summary>Preserves one-time compute preparation while allowing already borrowed native reuse.</summary>
    bool IGpuProgram.RequiresPreparation => !pipeline.IsValid;
    /// <summary>Identifies the compute executable's originating context.</summary>
    (nint Handle, long Generation) IGpuProgram.ExecutableContext => pipeline.ExecutableContext;
    /// <summary>Exposes stable lifecycle data solely to the common workflow.</summary>
    GpuProgramLifetime IGpuProgram.Lifetime => lifetime;

    /// <summary>Captures the enclosing managed generation through the existing tracker.</summary>
    IDisposable IGpuProgram.OpenUseScope(bool replayInputs) => StateCache.Current.CaptureProgramScope(replayInputs);

    /// <summary>Routes interface disposal through the same terminal facade.</summary>
    void IDisposable.Dispose() => Dispose();
    /// <summary>Releases the single compute executable owner after all activation borrows end.</summary>
    void IGpuProgram.ReleaseExecutable()
    {
        pipeline.Dispose();
    }
    /// <summary>Invokes extra-resource cleanup independently of native executable cleanup.</summary>
    void IGpuProgram.ReleaseResources() => ReleaseResources();

    #endregion

    #endregion
}
