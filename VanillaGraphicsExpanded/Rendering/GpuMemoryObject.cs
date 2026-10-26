using VanillaGraphicsExpanded.Rendering.RHI;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns a native MemoryObject allocation; the RHI backend executes resource operations.</summary>
public sealed class GpuMemoryObject : GpuResource<GpuMemoryObjectMetadata>
{
    private int memoryObjectId;
    #region Public API
    /// <inheritdoc />
    public override nint ResourceId { get => memoryObjectId; protected set => memoryObjectId = (int)value; }
    /// <summary>Gets native identity for explicit integration only.</summary>
    public int MemoryObjectId => memoryObjectId;
    /// <inheritdoc />
    public override void SetDebugName(string? name) { }
    /// <summary>Retains consumption authority without transferring native ownership.</summary>
    public static implicit operator GpuMemoryObjectRef(GpuMemoryObject? resource) => new(resource);
    #endregion
    #region Internal API
    /// <summary>Declares an unpublished owner for private manager handoff.</summary>
    internal GpuMemoryObject() { }
    #endregion
    #region Private
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.MemoryObject;
    #endregion
}
