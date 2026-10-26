using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Provides scoped CPU access only to successfully completed, request-owned staging.</summary>
public sealed class RHIMappedReadback : IDisposable
{
    private RHIReadbackTicket? owner;
    private readonly nint address;
    private readonly int length;
    #region Public API
    /// <summary>Gets read-only bytes while the original mapping and context remain active.</summary>
    public unsafe ReadOnlySpan<byte> Bytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(owner is null, this);
            owner.RequireMapping();
            return new((void*)address, length);
        }
    }
    /// <summary>Ends native mapping once without deleting or transferring staging ownership.</summary>
    public void Dispose()
    {
        if (owner is null) return;
        var current = owner;
        try { current.Unmap(); }
        finally { if (!current.HasActiveMapping) owner = null; }
    }
    #endregion
    #region Internal API
    /// <summary>Receives a successful ready mapping from its exclusive request owner.</summary>
    internal RHIMappedReadback(RHIReadbackTicket owner, nint address, int length)
    { this.owner = owner; this.address = address; this.length = length; }
    #endregion
}
