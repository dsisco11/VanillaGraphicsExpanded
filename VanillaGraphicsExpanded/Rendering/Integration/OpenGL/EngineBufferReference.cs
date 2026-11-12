using System;

namespace VanillaGraphicsExpanded.Rendering.Integration.OpenGL;

/// <summary>Receives canonical metadata for a source-proven native buffer without acquiring its ownership.</summary>
internal sealed class EngineBufferReference : GpuResource<GpuBufferMetadata>
{
    private static readonly GpuBufferMetadataReader Reader = new();
    private nint id;

    #region Public API
    /// <inheritdoc />
    public override nint ResourceId { get => id; protected set => id = value; }
    /// <summary>Leaves the native owner's labels unchanged.</summary>
    public override void SetDebugName(string? name) { }

    /// <summary>Associates an existing native allocation with the manager's single canonical metadata record.</summary>
    internal EngineBufferReference(int name)
    {
        if (name <= 0) throw new ArgumentOutOfRangeException(nameof(name));
        SetCreationIdentity(name, CaptureCreationContext(GpuResourceKind.Buffer));
        try { RHIResourceManager.AcquireWrapperMetadata(this, Reader); }
        catch { Dispose(); throw; }
    }

    /// <summary>Provides non-owning consumption authority tied to this original association.</summary>
    internal GpuBufferRef Reference => new(this);
    #endregion

    #region Protected API
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
    /// <inheritdoc />
    protected override bool OwnsResource => false;
    #endregion
}
