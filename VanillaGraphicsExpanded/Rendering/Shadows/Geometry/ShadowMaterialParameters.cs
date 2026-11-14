using System;
using System.Numerics;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns the complete std140 layout for shadow material parameters.</summary>
internal sealed class ShadowMaterialParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 208;
    internal const int MaterialTransformOffset = 0;
    internal const int ChunkOriginOffset = 64;
    internal const int AtlasPaddingXOffset = 76;
    internal const int DeformationWorldOriginOffset = 80;
    internal const int AtlasPaddingYOffset = 92;
    internal const int AnimationOffset = 96;
    internal const int WindOffset = 112;
    internal const int PerceptionOffset = 128;
    internal const int PerceptionModeOffset = 132;
    internal const int ShadowOriginTransformOffset = 144;
    internal const int ShadowOriginTranslationOffset = 192;
    internal const int MatrixFloatCount = 16;
    internal const int MatrixDiagonalStride = 5 * sizeof(float);

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Commits a material already checked by <see cref="Validate"/> during coordinated input admission.</summary>
    internal void SetValidatedMaterial(ReadOnlySpan<byte> material)
    {
        SetBytes(material);
    }

    /// <summary>Checks a material candidate before a caller coordinates publication with other blocks.</summary>
    internal static void Validate(ReadOnlySpan<byte> material)
    {
        if (material.Length != BlockSize) throw new ArgumentException("Material snapshot does not match its uniform layout.", nameof(material));
        if (!IsFinite(material)) throw new ArgumentException("Material snapshot contains nonfinite deformation input.", nameof(material));
    }

    /// <summary>Packs an object's frozen transform and precise virtual-origin translation into a complete candidate.</summary>
    internal static void PackObject(Span<byte> data, ReadOnlySpan<float> model,
        ShadowDeformationInputs deformation, Vector3d translation, float? windStrength = null)
    {
        RequireExtent(data, model);
        data.Clear();
        UboPacking.WriteMat4(data, MaterialTransformOffset, model[..MatrixFloatCount]);
        PackDeformation(data, deformation, 0, windStrength);
        // Object placement retains an identity origin transform with a separately prepared translation.
        for (int diagonal = 0; diagonal < 4; diagonal++)
            UboPacking.WriteFloat(data, ShadowOriginTransformOffset + diagonal * MatrixDiagonalStride, 1);
        UboPacking.WriteFloat(data, ShadowOriginTranslationOffset, (float)translation.X);
        UboPacking.WriteFloat(data, ShadowOriginTranslationOffset + sizeof(float), (float)translation.Y);
        UboPacking.WriteFloat(data, ShadowOriginTranslationOffset + 2 * sizeof(float), (float)translation.Z);
    }

    /// <summary>Packs terrain placement and deformation without changing its captured native reference frame.</summary>
    internal static void PackTerrain(Span<byte> data, ReadOnlySpan<float> view, ReadOnlySpan<float> transform,
        Vector3d origin, ShadowDeformationInputs deformation, float paddingX, float paddingY)
    {
        RequireExtent(data, view);
        if (transform.Length < MatrixFloatCount) throw new ArgumentException("Terrain transform requires a complete matrix.", nameof(transform));
        data.Clear();
        UboPacking.WriteMat4(data, MaterialTransformOffset, view[..MatrixFloatCount]);
        UboPacking.WriteMat4(data, ShadowOriginTransformOffset, transform[..MatrixFloatCount]);
        UboPacking.WriteVec4(data, ChunkOriginOffset, (float)origin.X, (float)origin.Y, (float)origin.Z, paddingX);
        PackDeformation(data, deformation, paddingY);
        if (!IsFinite(data)) throw new InvalidOperationException("Native shadow material contains nonfinite input.");
    }
    #endregion

    #region Private
    /// <summary>Writes shared deformation lanes while retaining the material-specific atlas padding.</summary>
    private static void PackDeformation(Span<byte> data, ShadowDeformationInputs input,
        float paddingY, float? windStrength = null)
    {
        UboPacking.WriteVec4(data, DeformationWorldOriginOffset,
            input.ReferenceOrigin.X, input.ReferenceOrigin.Y, input.ReferenceOrigin.Z, paddingY);
        UboPacking.WriteVec4(data, AnimationOffset, input.Animation.X, input.Animation.Y, input.Animation.Z, input.Animation.W);
        UboPacking.WriteVec4(data, WindOffset, input.Wind.X, input.Wind.Y, input.Wind.Z, windStrength ?? input.Wind.W);
        UboPacking.WriteVec4(data, PerceptionOffset, input.LiquidStrength, 0, input.PerceptionStrength, 0);
        UboPacking.WriteInt32(data, PerceptionModeOffset, input.PerceptionMode);
    }

    /// <summary>Checks every floating-point lane while keeping the integer perception selector opaque.</summary>
    private static bool IsFinite(ReadOnlySpan<byte> data)
    {
        for (int offset = 0; offset < data.Length; offset += sizeof(float))
            if (offset != PerceptionModeOffset && !float.IsFinite(UboPacking.ReadFloat(data, offset))) return false;
        return true;
    }

    /// <summary>Rejects partial packing destinations before any candidate bytes are written.</summary>
    private static void RequireExtent(Span<byte> data, ReadOnlySpan<float> matrix)
    {
        if (data.Length != BlockSize || matrix.Length < MatrixFloatCount)
            throw new ArgumentException("Material packing requires the complete layout and transform.");
    }
    #endregion
}
