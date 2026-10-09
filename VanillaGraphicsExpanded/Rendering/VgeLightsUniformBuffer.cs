using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains the engine point-light list in its original view space and color units.</summary>
internal sealed class VgeLightsUniformBuffer : CpuUniformBuffer
{
    #region Public API
    /// <summary>Preserves the engine's full supported dynamic-light capacity.</summary>
    internal const int Capacity = 100;
    /// <summary>One unsigned count with std140 alignment padding followed by two padded vector arrays.</summary>
    internal const int PackedSize = 16 + Capacity * 32;
    /// <summary>Allocates retained packing with immutable versioned GPU slices.</summary>
    internal VgeLightsUniformBuffer() : base(PackedSize) { }

    /// <summary>Captures a complete light list without publishing partially validated input.</summary>
    internal void Capture(int count, ReadOnlySpan<float> positions, ReadOnlySpan<float> colors)
    {
        if ((uint)count > Capacity) throw new ArgumentOutOfRangeException(nameof(count));
        if (positions.Length < count * 3 || colors.Length < count * 3)
            throw new ArgumentException("Every active light requires a position and a color.");
        // Retain engine calibration and coordinates exactly. Reject malformed active components
        // before touching the previous snapshot; inactive storage is never exposed by the count.
        for (int component = 0; component < count * 3; component++)
            if (!float.IsFinite(positions[component]) || !float.IsFinite(colors[component]))
                throw new ArgumentException("Light components must be finite.");
        WriteUInt32(0, (uint)count);
        for (int light = 0; light < count; light++)
        {
            int source = light * 3;
            WriteVector4(16 + light * 16, new(positions[source], positions[source + 1], positions[source + 2], 0));
            WriteVector4(16 + Capacity * 16 + light * 16, new(colors[source], colors[source + 1], colors[source + 2], 0));
        }
    }
    #endregion
}
