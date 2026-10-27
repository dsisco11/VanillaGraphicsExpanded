using VanillaGraphicsExpanded.Rendering.RHI;
using System;


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
    /// Sets the debug label for this pipeline (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
        RHICommandContext.TryLabelResource(this, debugName);
    }

    #endregion
    #endregion

    #region Protected API
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.ProgramPipeline;
    #endregion

    #region Internal API
    /// <summary>Declares a native-free recipient for explicit integration initialization.</summary>
    internal GpuProgramPipeline() { }
    #endregion
}
