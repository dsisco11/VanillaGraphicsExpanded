using VanillaGraphicsExpanded.Rendering.RHI;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns a native TransformFeedback allocation; the RHI backend executes resource operations.</summary>
public sealed class GpuTransformFeedback : GpuResource<GpuTransformFeedbackMetadata>
{
    private int transformFeedbackId;
    #region Public API
    /// <inheritdoc />
    public override nint ResourceId { get => transformFeedbackId; protected set => transformFeedbackId = (int)value; }
    /// <summary>Gets native identity for explicit integration only.</summary>
    public int TransformFeedbackId => transformFeedbackId;
    /// <inheritdoc />
    public override void SetDebugName(string? name) { }
    /// <summary>Retains consumption authority without transferring native ownership.</summary>
    public static implicit operator GpuTransformFeedbackRef(GpuTransformFeedback? resource) => new(resource);
    #endregion
    #region Internal API
    /// <summary>Declares an unpublished owner for private manager handoff.</summary>
    internal GpuTransformFeedback() { }
    #endregion
    #region Private
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.TransformFeedback;
    #endregion
}
