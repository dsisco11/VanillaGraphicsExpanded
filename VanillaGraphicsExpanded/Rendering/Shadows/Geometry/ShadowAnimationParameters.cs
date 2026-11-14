using System;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns the complete std140 layout for shadow animation parameters.</summary>
internal sealed class ShadowAnimationParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = JointCapacity * MatrixStride;

    internal const int JointCapacity = 230;
    internal const int MatrixFloatCount = 16;
    internal const int MatrixStride = MatrixFloatCount * sizeof(float);
    internal const int PaletteFloatCount = JointCapacity * MatrixFloatCount;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Checks the complete supplied palette before coordinated material publication.</summary>
    internal static void Validate(ReadOnlySpan<float> joints)
    {
        if (joints.Length < MatrixFloatCount || joints.Length % MatrixFloatCount != 0 || joints.Length > PaletteFloatCount)
            throw new ArgumentException("Animation palette has an unsupported extent.", nameof(joints));
        foreach (float value in joints)
            if (!float.IsFinite(value)) throw new ArgumentException("Animation palette is not finite.", nameof(joints));
    }

    /// <summary>Commits a palette checked by <see cref="Validate"/>, clearing joints absent from the current object.</summary>
    internal void SetValidatedPalette(ReadOnlySpan<float> joints)
    {
        Span<byte> data = stackalloc byte[BlockSize];
        data.Clear();
        MemoryMarshal.AsBytes(joints).CopyTo(data);
        SetBytes(data);
    }
    #endregion
}
