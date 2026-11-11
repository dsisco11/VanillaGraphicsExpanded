using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Defines conventional projected depth's ordered integer representation for atomic minimum.</summary>
internal static class ShadowDepthEncoding
{
    /// <summary>Empty texels compare as far-plane depth.</summary>
    internal const uint Clear = 0x3f800000;

    #region Public API
    /// <summary>Rejects invalid projected values and canonicalizes zero before ordered unsigned storage.</summary>
    internal static uint Encode(float depth)
    {
        if (!float.IsFinite(depth) || depth < 0 || depth > 1) throw new ArgumentOutOfRangeException(nameof(depth));
        return BitConverter.SingleToUInt32Bits(depth == 0 ? 0 : depth);
    }

    /// <summary>Rejects bit patterns outside the supported nonnegative finite depth interval.</summary>
    internal static float Decode(uint depth)
    {
        if (depth > Clear) throw new ArgumentOutOfRangeException(nameof(depth));
        return BitConverter.UInt32BitsToSingle(depth);
    }
    #endregion
}
