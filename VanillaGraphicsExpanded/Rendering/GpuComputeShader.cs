using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns retained compute inputs and publishes them before executing work.</summary>
internal abstract class GpuComputeShader : IShaderSubmissionTarget, IDisposable
{
    protected readonly GpuComputePipeline pipeline;
    private bool submitting;
    private bool disposed;
    [ThreadStatic] private static Dictionary<int, WeakReference<GpuComputeShader>>? owners;

    #region Public API
    #region Executable state
    /// <summary>Gets the current executable identifier.</summary>
    public int ProgramId => pipeline.ProgramId;
    /// <summary>Gets the installed binding layout.</summary>
    public GpuProgramLayout ProgramLayout => pipeline.ProgramLayout;
    /// <summary>Reports whether the owned executable remains available.</summary>
    public bool IsValid => !disposed && pipeline.IsValid;
    /// <summary>Reports the owned executable's last preparation failure.</summary>
    public string PreparationLog => pipeline.PreparationLog;
    /// <summary>Prepares the executable without publishing inputs or executing work.</summary>
    public bool EnsureReady()
    {
        RequireInputMutation();
        return pipeline.EnsureReady();
    }
    #endregion

    #region Submission and execution
    /// <summary>Activates the executable and publishes the complete retained input set.</summary>
    public void Use()
    {
        RequireInputMutation();
        ShaderProgramBase.CurrentShaderProgram?.Stop();
        submitting = true;
        try
        {
            pipeline.Use();
            Submit();
            (owners ??= new())[ProgramId] = new(this);
        }
        catch { GlStateCache.Current.UnbindProgram(); throw; }
        finally { submitting = false; }
    }

    /// <summary>Publishes inputs and restores the previous executable on scope exit.</summary>
    public IDisposable UseScope()
    {
        RequireInputMutation();
        var previous = ShaderProgramBase.CurrentShaderProgram;
        int previousId = GlStateCache.Current.GetCurrentProgram();
        // Retain owner identity before nested work can dispose it or recycle its GL name.
        var scope = new SubmissionScope(previous, previousId, FindOwner(previousId));
        try { Use(); return scope; }
        catch { scope.Dispose(); throw; }
    }

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
    public virtual void Dispose()
    {
        if (disposed) return;
        RequireInputMutation();
        disposed = true;
        owners?.Remove(ProgramId);
        pipeline.Dispose();
    }

    /// <summary>Captures compute ownership so scopes cannot mistake a retired owner for a raw executable.</summary>
    internal static GpuComputeShader? FindOwner(int program)
    {
        return owners != null && owners.TryGetValue(program, out var reference) && reference.TryGetTarget(out var owner)
            ? owner : null;
    }
    #endregion
    #endregion

    #region Protected API
    /// <summary>Adopts the existing compute executable and its preparation policy.</summary>
    protected GpuComputeShader(GpuComputePipeline pipeline) => this.pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    /// <summary>Allows derived resource owners to preserve idempotent disposal.</summary>
    protected bool IsDisposed => disposed;
    /// <summary>Rejects input writes during submission or after disposal.</summary>
    protected void RequireInputMutation()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (submitting) throw new InvalidOperationException("Compute inputs cannot change during submission.");
    }
    /// <summary>Implemented by generated binding-contract publication.</summary>
    protected abstract void Submit();
    #endregion

    #region Private
    /// <summary>Restores graphics and compute ownership as well as the underlying GL executable.</summary>
    private sealed class SubmissionScope(ShaderProgramBase? previous, int program, GpuComputeShader? compute) : IDisposable
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
                else GlStateCache.Current.UseProgram(program);
            }
            catch { GlStateCache.Current.UnbindProgram(); throw; }
        }
    }
    #endregion
}
