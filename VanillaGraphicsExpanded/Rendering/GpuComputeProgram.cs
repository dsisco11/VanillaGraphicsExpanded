using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns retained compute inputs and publishes them before executing work.</summary>
internal abstract class GpuComputeProgram : IGpuProgram
{
    protected readonly GpuComputePipeline pipeline;
    private readonly GpuProgramLifetime lifetime = new();
    [ThreadStatic] private static Dictionary<int, WeakReference<GpuComputeProgram>>? owners;

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

    #region Internal API
    /// <summary>Captures compute ownership so scopes cannot mistake a retired owner for a raw executable.</summary>
    internal static GpuComputeProgram? FindOwner(int program)
    {
        return owners != null && owners.TryGetValue(program, out var reference) && reference.TryGetTarget(out var owner)
            ? owner : null;
    }
    #endregion

    #region Protected API
    /// <summary>Adopts the existing compute executable and its preparation policy.</summary>
    protected GpuComputeProgram(GpuComputePipeline pipeline) => this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
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

    /// <summary>Preserves the existing native handoff until scoped ownership migration.</summary>
    void IGpuProgram.BindExecutable()
    {
        ShaderProgramBase.CurrentShaderProgram?.Stop();
        StateCache.Current.UseProgram(ProgramId);
    }
    /// <summary>Publishes generated inputs and retains the temporary restoration lookup.</summary>
    void IGpuProgram.PublishInputs()
    {
        Submit();
        (owners ??= new())[ProgramId] = new(this);
    }
    /// <summary>Unbinds an incomplete compute activation.</summary>
    void IGpuProgram.ClearActivation(bool bindingEntered)
    {
        if (bindingEntered) StateCache.Current.UnbindProgram();
    }
    /// <summary>Exposes stable lifecycle data solely to the common workflow.</summary>
    GpuProgramLifetime IGpuProgram.Lifetime => lifetime;

    /// <summary>Preserves existing compute restoration until program scopes migrate.</summary>
    IDisposable IGpuProgram.OpenUseScope()
    {
        RequireInputMutation();
        var previous = ShaderProgramBase.CurrentShaderProgram;
        int previousId = StateCache.Current.GetCurrentProgram();
        // Retain owner identity before nested work can dispose it or recycle its GL name.
        return new SubmissionScope(previous, previousId, FindOwner(previousId));
    }

    /// <summary>Routes interface disposal through the same terminal facade.</summary>
    void IDisposable.Dispose() => Dispose();
    /// <summary>Releases the single compute executable owner and temporary restoration lookup.</summary>
    void IGpuProgram.ReleaseExecutable()
    {
        owners?.Remove(ProgramId);
        pipeline.Dispose();
    }
    /// <summary>Invokes extra-resource cleanup independently of native executable cleanup.</summary>
    void IGpuProgram.ReleaseResources() => ReleaseResources();

    #endregion

    #region Restoration
    /// <summary>Restores graphics and compute ownership as well as the underlying GL executable.</summary>
    private sealed class SubmissionScope(ShaderProgramBase? previous, int program, GpuComputeProgram? compute) : IDisposable
    {
        private bool ended;
        /// <summary>Re-publishes the enclosing owner's resources once on scope exit.</summary>
        public void Dispose()
        {
            if (ended) return;
            ended = true;
            try
            {
                if (previous is GpuProgram graphics) graphics.Use();
                else if (previous != null) previous.Use();
                else if (compute != null) compute.Use();
                else StateCache.Current.UseProgram(program);
            }
            catch { StateCache.Current.UnbindProgram(); throw; }
        }
    }
    #endregion
    #endregion
}
