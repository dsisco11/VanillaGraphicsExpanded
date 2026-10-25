using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL program pipeline object (separable program stages).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuProgramPipeline : GpuResource<GpuProgramPipelineMetadata>, IDisposable
{
    private int pipelineId;

    #region Public API
    #region Identity and creation
    /// <inheritdoc />
    public override nint ResourceId
    {
        get => pipelineId;
        protected set => pipelineId = (int)value;
    }

    /// <summary>
    /// Gets the underlying OpenGL program pipeline id.
    /// </summary>
    public int PipelineId => pipelineId;

    /// <summary>
    /// Returns <c>true</c> when the pipeline has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => base.IsValid;

    /// <summary>
    /// Creates a new program pipeline object.
    /// </summary>
    public static GpuProgramPipeline Create(string? debugName = null)
    {
        var value = new GpuProgramPipeline();
        try
        {
            if (!GpuResourceManager.CreateProgramPipeline(new GpuProgramPipelineInitialization(), value, out _).HasValue)
                throw new InvalidOperationException("ProgramPipeline creation failed.");
            value.SetDebugName(debugName);
            return value;
        }
        catch
        {
            // The manager retires provisional allocation; dispose a completed handoff too.
            value.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Sets the debug label for this pipeline (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (pipelineId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.ProgramPipeline, pipelineId, debugName);
        }
#endif
    }

    #endregion
    #region Binding

    /// <summary>
    /// Binds this pipeline via <c>glBindProgramPipeline</c>.
    /// </summary>
    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuProgramPipeline] Attempted to bind disposed or invalid pipeline");
            return;
        }

        StateCache.Current.BindProgramPipeline(pipelineId);
    }

    /// <summary>
    /// Attempts to bind this pipeline. Returns <c>false</c> if invalid.
    /// </summary>
    public bool TryBind()
    {
        if (!IsValid)
        {
            return false;
        }

        StateCache.Current.BindProgramPipeline(pipelineId);
        return true;
    }

    /// <summary>
    /// Unbinds any program pipeline (binds 0).
    /// </summary>
    public static void Unbind()
    {
        StateCache.Current.BindProgramPipeline(0);
    }

    /// <summary>
    /// Binds this pipeline and returns a scope that restores the previous pipeline binding when disposed.
    /// </summary>
    public BindingScope BindScope()
    {
        ObjectDisposedException.ThrowIf(!IsValid, this);
        var gl = StateCache.Current;
        var scope = gl.BindProgramPipelineScope(pipelineId);
        return new BindingScope(scope);
    }

    #endregion
    #region Configuration and validation

    /// <summary>
    /// Assigns separable program stages to this pipeline via <c>glUseProgramStages</c>.
    /// </summary>
    /// <remarks>
    /// The provided program id must be a linked separable program (GL_PROGRAM_SEPARABLE = true).
    /// </remarks>
    public void UseStages(ProgramStageMask stages, int programId)
    {
        if (!IsValid)
        {
            return;
        }

        // This command names its target pipeline and does not use the selected pipeline.
        GL.UseProgramStages(pipelineId, stages, programId);
    }

    /// <summary>
    /// Sets the active program for subsequent uniform updates when using a pipeline via <c>glActiveShaderProgram</c>.
    /// </summary>
    public void ActiveShaderProgram(int programId)
    {
        if (!IsValid)
        {
            return;
        }

        // Uniform-target selection is per pipeline; changing the bound pipeline is unnecessary.
        GL.ActiveShaderProgram(pipelineId, programId);
    }

    /// <summary>
    /// Validates this pipeline via <c>glValidateProgramPipeline</c>.
    /// </summary>
    /// <param name="infoLog">Pipeline info log, if available.</param>
    /// <returns>True if validation succeeded.</returns>
    public bool TryValidate(out string infoLog)
    {
        infoLog = string.Empty;

        if (!IsValid)
        {
            return false;
        }

        try
        {
            GL.ValidateProgramPipeline(pipelineId);
            GL.GetProgramPipeline(pipelineId, ProgramPipelineParameter.ValidateStatus, out int status);

            try
            {
                GL.GetProgramPipeline(pipelineId, ProgramPipelineParameter.InfoLogLength, out int logLen);
                if (logLen > 1)
                {
                    GL.GetProgramPipelineInfoLog(pipelineId, logLen, out _, out string log);
                    infoLog = log ?? string.Empty;
                }
            }
            catch
            {
                // Best-effort only.
                infoLog = string.Empty;
            }

            return status != 0;
        }
        catch
        {
            return false;
        }
    }

    #endregion
    #region Binding scope

    /// <summary>
    /// Scope that restores the previous program pipeline binding when disposed.
    /// </summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly StateCache.ProgramPipelineScope scope;

        #region Public API
        /// <summary>Retains the cache scope which owns restoration of the previous native selection.</summary>
        public BindingScope(StateCache.ProgramPipelineScope scope)
        {
            this.scope = scope;
        }

        /// <summary>Restores the program pipeline selection captured on entry.</summary>
        public void Dispose()
        {
            scope.Dispose();
        }
        #endregion
    }
    #endregion
    #endregion

    #region Protected API
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.ProgramPipeline;
    #endregion

    #region Private
    /// <summary>Declares an unpublished wrapper for manager-owned creation.</summary>
    private GpuProgramPipeline() { }
    #endregion
}
