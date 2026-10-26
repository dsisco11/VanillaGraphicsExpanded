namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Publishes semantic buffer selections through the shared backend/cache contract.</summary>
public sealed partial class RHICommandContext
{
    #region Public API
    /// <summary>Selects a live buffer for a declared role, or clears that selection with an empty ref.</summary>
    public void BindBuffer(RHIBufferRole role, GpuBufferRef buffer) => backend.BindBuffer(role, buffer);
    /// <summary>Selects a complete indexed buffer, including its generic-selector side effect.</summary>
    public void BindBufferBase(RHIBufferRole role, int slot, GpuBufferRef buffer) => backend.BindBufferBase(role, slot, buffer);
    /// <summary>Selects a validated indexed range in allocation-relative byte coordinates.</summary>
    public void BindBufferRange(RHIBufferRole role, int slot, GpuBufferRef buffer, nint offset, nint size) => backend.BindBufferRange(role, slot, buffer, offset, size);
    #endregion
}
