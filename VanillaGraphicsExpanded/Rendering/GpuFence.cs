using System;
using VanillaGraphicsExpanded.Rendering.RHI;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns a native-width fence; the RHI context executes synchronization.</summary>
public sealed class GpuFence : GpuResource<GpuSyncMetadata>
{
    private nint handle;
    #region Public API
    /// <inheritdoc />
    public override nint ResourceId { get => handle; protected set => handle = value; }
    /// <inheritdoc />
    public override void SetDebugName(string? debugName) { }
    /// <summary>Creates a consumer reference without native discovery.</summary>
    public static implicit operator GpuSyncRef(GpuFence? resource) => new(resource);
    #endregion
    #region Internal API
    /// <summary>Releases staging completion ownership only after a successful wait.</summary>
    internal bool TryConsumeIfSignaled()
    {
        var status = RHICommandContext.Current.Poll(this);
        if (status == RHICompletionStatus.Pending) return false;
        if (status != RHICompletionStatus.Ready) throw new InvalidOperationException("Fence completion failed.");
        Dispose();
        return true;
    }
    /// <summary>Declares an unpublished owner for manager handoff.</summary>
    internal GpuFence() { }
    #endregion
    #region Private
    /// <inheritdoc />
    protected override GpuResourceKind ResourceKind => GpuResourceKind.Sync;
    #endregion
}
