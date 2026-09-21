using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>GPU-ready shared voxel in source X/Z/Y order; geometry is independent of material readiness.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct TraceGeometryVoxel(uint Geometry, uint LegacyLight, uint NormalizedLight)
{
    public const int Bytes = 12;
    public uint Kind => Geometry & 3;
    public uint Material => Geometry >> 2;

    /// <summary>Quantizes the existing normalized light representation to four unsigned bytes.</summary>
    public static uint PackLight(in Vector4 light) => Byte(light.X) | Byte(light.Y) << 8 | Byte(light.Z) << 16 | Byte(light.W) << 24;

    /// <summary>Uses nearest normalized-byte quantization without allowing values outside the texture range.</summary>
    private static uint Byte(float value) => (uint)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
}
