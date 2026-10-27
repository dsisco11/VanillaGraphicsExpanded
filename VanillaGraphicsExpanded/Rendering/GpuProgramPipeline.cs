using System;

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
