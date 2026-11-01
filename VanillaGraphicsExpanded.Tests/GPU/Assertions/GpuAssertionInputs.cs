using System.Numerics;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Packs the fixed std140 observation block without narrowing numeric expectations.</summary>
internal static class GpuAssertionInputs
{
    #region Internal API
    /// <summary>Writes one explicitly initialized signed or unsigned 32-bit field.</summary>
    internal static void Integer(byte[] bytes, int offset, uint value) => BitConverter.TryWriteBytes(bytes.AsSpan(offset, 4), value);
    /// <summary>Writes the original double bits, including open interval endpoints.</summary>
    internal static void Double(byte[] bytes, int offset, double value) => BitConverter.TryWriteBytes(bytes.AsSpan(offset, 8), value);
    /// <summary>Writes all four vector components into their std140 slot.</summary>
    internal static void Vector(byte[] bytes, int offset, Vector4 value)
    {
        for (int channel = 0; channel < 4; channel++)
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + channel * 4, 4), value[channel]);
    }
    #endregion
}
