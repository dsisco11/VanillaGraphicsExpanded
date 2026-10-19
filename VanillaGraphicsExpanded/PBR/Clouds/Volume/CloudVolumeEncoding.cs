using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Pairs a bounded physical diffuse source with a power-of-two half-float representation.</summary>
internal readonly record struct CloudVolumeEncoding
{
    internal Vector3 Cap { get; }
    internal float Scale { get; }

    #region Public API
    /// <summary>Admits componentwise nonnegative float32 bounds and reserves half-float storage range.</summary>
    internal CloudVolumeEncoding(Vector3 cap)
    {
        if (!Finite(cap) || Vector3.Min(cap, Vector3.Zero) != Vector3.Zero) throw new ArgumentOutOfRangeException(nameof(cap));
        double peak = Math.Max(cap.X, Math.Max(cap.Y, cap.Z));
        Scale = (float)Math.Pow(2, Math.Ceiling(Math.Log2(Math.Max(1, peak) / 32752)));
        Cap = cap;
        if (!float.IsFinite(Scale) || Scale <= 0 || !float.IsFinite(1 / Scale) || 1 / Scale == 0)
            throw new ArgumentOutOfRangeException(nameof(cap));
    }

    /// <summary>Tests records before they describe a published field, including rejecting the default value.</summary>
    internal bool IsValid => Scale > 0 && float.IsFinite(Scale) && float.IsFinite(1 / Scale) &&
        BitOperations.IsPow2((uint)BitConverter.SingleToInt32Bits(Scale) & 0x7fffffu | 0x800000u) &&
        Finite(Cap) && Vector3.Min(Cap, Vector3.Zero) == Vector3.Zero &&
        Math.Max(Cap.X, Math.Max(Cap.Y, Cap.Z)) / (double)Scale <= 32752;

    /// <summary>Encodes a finite source downward so repeated publication cannot exceed its physical cap.</summary>
    internal Vector3 Encode(Vector3 physical)
    {
        if (!IsValid || !Finite(physical) || Vector3.Min(physical, Vector3.Zero) != Vector3.Zero)
            throw new ArgumentOutOfRangeException(nameof(physical));
        Vector3 value = Vector3.Min(physical, Cap) / Scale;
        return new(Down(value.X), Down(value.Y), Down(value.Z));
    }
    #endregion
    #region Private
    /// <summary>Uses the predecessor of an upward-rounded nonnegative half, including subnormal values.</summary>
    private static float Down(float value)
    {
        Half half = (Half)value;
        return (float)half <= value ? (float)half : (float)BitConverter.UInt16BitsToHalf((ushort)(BitConverter.HalfToUInt16Bits(half) - 1));
    }
    /// <summary>Checks all physical channels before arithmetic or storage conversion.</summary>
    private static bool Finite(Vector3 value) => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
    #endregion
}
