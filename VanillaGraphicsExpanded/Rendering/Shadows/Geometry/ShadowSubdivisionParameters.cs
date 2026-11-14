using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Owns the complete std140 layout for shadow subdivision parameters.</summary>
internal sealed class ShadowSubdivisionParameters : CpuUniformBuffer
{
    #region Public API
    internal const int BlockSize = 32;
    internal const int PixelsOffset = 0;
    internal const int DistanceOffset = 8;
    internal const int FocalPixelsOffset = 16;
    internal const int EnabledOffset = 20;

    /// <summary>Declares this layout's immutable byte extent.</summary>
    public override int SizeBytes => BlockSize;

    /// <summary>Checks subdivision values before the shader changes associated resource bindings.</summary>
    internal static void Validate(float edgePixels, float maximumLevel, float fadeStart, float fadeEnd, float focalPixels)
    {
        if (!float.IsFinite(edgePixels) || edgePixels <= 0 || !float.IsFinite(maximumLevel) || maximumLevel is < 1 or > 8 ||
            !float.IsFinite(fadeStart) || fadeStart < 0 || !float.IsFinite(fadeEnd) || fadeEnd <= fadeStart ||
            !float.IsFinite(focalPixels) || focalPixels <= 0)
            throw new System.ArgumentException("Displacement requires finite native subdivision inputs.");
    }

    /// <summary>Stages controls checked by <see cref="Validate"/> after the shader admits their resources.</summary>
    internal void SetValidated(float edgePixels, float maximumLevel, float fadeStart, float fadeEnd,
        float focalPixels, bool enabled)
    {
        WriteVector4(PixelsOffset, new(edgePixels, maximumLevel, fadeStart, fadeEnd));
        WriteFloat(FocalPixelsOffset, focalPixels);
        WriteInt32(EnabledOffset, enabled ? 1 : 0);
    }
    #endregion
}
