using System;
using System.Collections.Immutable;

namespace VanillaGraphicsExpanded.Rendering.RHI;

/// <summary>Retains opaque backend-produced executable cache bytes; cache selection belongs to the shader owner.</summary>
public sealed class RHIShaderBinary
{
    #region Public API
    /// <summary>Gets a copied immutable executable payload.</summary>
    public ImmutableArray<byte> Bytes { get; }
    #endregion

    #region Internal API
    /// <summary>Gets the originating OpenGL driver's binary-format identifier.</summary>
    internal int NativeFormat { get; }
    /// <summary>Captures a validated cache input or a backend extraction without exposing a native format selector.</summary>
    internal RHIShaderBinary(int nativeFormat, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty) throw new ArgumentException("An executable binary must contain bytes.", nameof(bytes));
        NativeFormat = nativeFormat; Bytes = ImmutableArray.Create(bytes.ToArray());
    }
    #endregion
}
