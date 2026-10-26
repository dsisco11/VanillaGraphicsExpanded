using System;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Supplies stable immediate client content independently of allocation metadata.</summary>
public readonly ref struct RHIBufferInitialData
{
    #region Public API
    /// <summary>Gets the destination byte offset.</summary>
    public nint Offset { get; }
    /// <summary>Gets bytes whose lifetime extends through the immediate factory call.</summary>
    public ReadOnlySpan<byte> Bytes { get; }
    /// <summary>Declares an explicit initial range without retaining the caller's memory.</summary>
    public RHIBufferInitialData(ReadOnlySpan<byte> bytes, nint offset = 0) { Bytes = bytes; Offset = offset; }
    #endregion
}
