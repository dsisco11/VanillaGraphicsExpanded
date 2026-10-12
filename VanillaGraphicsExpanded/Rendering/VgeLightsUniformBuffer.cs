using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains original engine payloads or explicitly authored candela in the shared view-space light list.</summary>
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
    /// <summary>Publishes VGE-authored luminous flux through the retained list without relabeling engine RGB.</summary>
    internal void CaptureAuthored(ReadOnlySpan<PBR.PunctualLight> sources)
    {
        if (sources.Length > Capacity) throw new ArgumentOutOfRangeException(nameof(sources));
        Span<System.Numerics.Vector4> payloads = stackalloc System.Numerics.Vector4[sources.Length];
        // Validate the complete generation before mutating its count or any exposed vector.
        for (int i = 0; i < sources.Length; i++) payloads[i] = sources[i].IntensityPayload();
        WriteUInt32(0, (uint)sources.Length);
        for (int i = 0; i < sources.Length; i++)
        {
            WriteVector4(16 + i * 16, new(sources[i].Position, 0));
            WriteVector4(16 + Capacity * 16 + i * 16, payloads[i]);
        }
    }
    #endregion
}
