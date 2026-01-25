using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL program pipeline object (separable program stages).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuProgramPipeline : GpuResource, IDisposable
{
    private int pipelineId;

    protected override nint ResourceId
    {
        get => pipelineId;
        set => pipelineId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.ProgramPipeline;

    /// <summary>
    /// Gets the underlying OpenGL program pipeline id.
    /// </summary>
    public int PipelineId => pipelineId;

    /// <summary>
    /// Returns <c>true</c> when the pipeline has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => pipelineId != 0 && !IsDisposed;

    private GpuProgramPipeline(int pipelineId)
    {
        this.pipelineId = pipelineId;
    }

    /// <summary>
    /// Creates a new program pipeline object.
    /// </summary>
    public static GpuProgramPipeline Create(string? debugName = null)
    {
        int id = GL.GenProgramPipeline();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenProgramPipelines failed.");
        }

        var pipeline = new GpuProgramPipeline(id);
        pipeline.SetDebugName(debugName);
        return pipeline;
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

        GlStateCache.Current.BindProgramPipeline(pipelineId);
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

        GlStateCache.Current.BindProgramPipeline(pipelineId);
        return true;
    }

    /// <summary>
    /// Unbinds any program pipeline (binds 0).
    /// </summary>
    public static void Unbind()
    {
        GlStateCache.Current.BindProgramPipeline(0);
    }

    /// <summary>
    /// Binds this pipeline and returns a scope that restores the previous pipeline binding when disposed.
    /// </summary>
    public BindingScope BindScope()
    {
        var gl = GlStateCache.Current;
        var scope = gl.BindProgramPipelineScope(pipelineId);
        return new BindingScope(scope);
    }

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

        using var _ = BindScope();
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

        using var _ = BindScope();
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

    /// <summary>
    /// Scope that restores the previous program pipeline binding when disposed.
    /// </summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly GlStateCache.ProgramPipelineScope scope;

        public BindingScope(GlStateCache.ProgramPipelineScope scope)
        {
            this.scope = scope;
        }

        public void Dispose()
        {
            scope.Dispose();
        }
    }
}
