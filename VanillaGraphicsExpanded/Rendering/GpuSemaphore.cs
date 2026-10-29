using VanillaGraphicsExpanded.Rendering.RHI;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns a native Semaphore allocation; the RHI backend executes resource operations.</summary>
public sealed class GpuSemaphore : GpuResource<GpuSemaphoreMetadata>
{
    private int semaphoreId;
    #region Public API
    /// <inheritdoc />
    public override nint ResourceId { get => semaphoreId; protected set => semaphoreId = (int)value; }
    /// <summary>Gets native identity for explicit integration only.</summary>
    public int SemaphoreId => semaphoreId;
    /// <inheritdoc />
    public override void SetDebugName(string? name) { }
    /// <summary>Retains consumption authority without transferring native ownership.</summary>
    public static implicit operator GpuSemaphoreRef(GpuSemaphore? resource) => new(resource);
    #endregion
    #region Internal API
    /// <summary>Declares an unpublished owner for private manager handoff.</summary>
    internal GpuSemaphore(bool importRequested = false) { ImportRequested = importRequested; }
    /// <summary>Retains immutable creation intent for context-local owners without a registry.</summary>
    internal bool ImportRequested { get; }
    #endregion
    #region Private
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Semaphore;
    #endregion
}
