using System;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Retains original engine payloads or explicitly authored candela in the shared view-space light list.</summary>
internal sealed class VgeLightsUniformBuffer : CpuUniformBuffer
{
    #region Public API
    #region Layout
    internal const int LightCountOffset = 0;
    internal const int PositionsOffset = 16;
    internal const int VectorStride = 16;
    internal const int ColorsOffset = PositionsOffset + Capacity * VectorStride;
    #endregion

    /// <summary>Declares the immutable byte extent owned by this layout.</summary>
    public override int SizeBytes => PackedSize;

    /// <summary>Preserves the engine's full supported dynamic-light capacity.</summary>
    internal const int Capacity = 100;
    /// <summary>One unsigned count with std140 alignment padding followed by two padded vector arrays.</summary>
    internal const int PackedSize = ColorsOffset + Capacity * VectorStride;
    /// <summary>Allocates retained packing with immutable versioned GPU slices.</summary>
    internal VgeLightsUniformBuffer() : base() { }
    /// <summary>Bounds summed active directional RGB intensity in candela for the matching immutable light list.</summary>
    internal float SummedCandelaMaximum { get; private set; }
    /// <summary>Matches native effect color/normalization admission to the same immutable engine payload.</summary>
    internal PBR.SceneColor.SceneColorPointLightBounds NativeEffectBounds { get; private set; }
    /// <summary>Pairs the immutable native or authored payload with the same captured source identities.</summary>
    internal ImmutableArray<ShadowPointLight> Lights { get; private set; } = ImmutableArray<ShadowPointLight>.Empty;

    /// <summary>Captures a complete light list without publishing partially validated input.</summary>
    internal void Capture(int count, ReadOnlySpan<float> positions, ReadOnlySpan<float> colors,
        ReadOnlySpan<ShadowSourceKey> sources = default, ReadOnlySpan<Vector3d?> worldPositions = default)
    {
        if ((uint)count > Capacity) throw new ArgumentOutOfRangeException(nameof(count));
        if (positions.Length < count * 3 || colors.Length < count * 3)
            throw new ArgumentException("Every active light requires a position and a color.");
        if (!sources.IsEmpty && sources.Length != count) throw new ArgumentException("Source count must match the light payload.", nameof(sources));
        if (!worldPositions.IsEmpty && worldPositions.Length != count) throw new ArgumentException("World positions must match the light payload.", nameof(worldPositions));
        // Retain engine calibration and coordinates exactly. Reject malformed active components
        // before touching the previous snapshot; inactive storage is never exposed by the count.
        for (int component = 0; component < count * 3; component++)
            if (!float.IsFinite(positions[component]) || !float.IsFinite(colors[component]))
                throw new ArgumentException("Light components must be finite.");
        var calibration = VgeFrameRenderer.CurrentLightingCalibration;
        double red = 0, green = 0, blue = 0;
        for (int light = 0; light < count; light++)
        {
            red += Math.Max(colors[light * 3], 0) * (double)calibration.PointCandelaPerUnit;
            green += Math.Max(colors[light * 3 + 1], 0) * (double)calibration.PointCandelaPerUnit;
            blue += Math.Max(colors[light * 3 + 2], 0) * (double)calibration.PointCandelaPerUnit;
        }
        // Bound the shader's multiplication and accumulated source sum outward before
        // exposing any light. Position, window and inverse-square terms cannot amplify it.
        float candelaMaximum = PBR.LightingStorage.UpperBound(Math.Max(red, Math.Max(green, blue)) * (1 + 256.0 / (1 << 23)));
        var nativeBounds = PBR.SceneColor.SceneColorPointLightBounds.FromEngine(count, colors);
        var snapshot = ImmutableArray.CreateBuilder<ShadowPointLight>(count);
        WriteUInt32(LightCountOffset, (uint)count);
        for (int light = 0; light < count; light++)
        {
            int source = light * 3;
            var worldPosition = worldPositions.IsEmpty ? null : worldPositions[light];
            if (worldPosition is { } world && (!double.IsFinite(world.X) || !double.IsFinite(world.Y) || !double.IsFinite(world.Z)))
                worldPosition = null;
            WriteVector4(PositionsOffset + light * VectorStride, new(positions[source], positions[source + 1], positions[source + 2], 0));
            WriteVector4(ColorsOffset + light * VectorStride, new(colors[source], colors[source + 1], colors[source + 2], 0));
            snapshot.Add(new(sources.IsEmpty ? default : sources[light],
                new(positions[source], positions[source + 1], positions[source + 2]),
                new(colors[source], colors[source + 1], colors[source + 2], 0), worldPosition));
        }
        SummedCandelaMaximum = candelaMaximum;
        NativeEffectBounds = nativeBounds;
        Lights = snapshot.MoveToImmutable();
    }
    /// <summary>Publishes VGE-authored luminous flux through the retained list without relabeling engine RGB.</summary>
    internal void CaptureAuthored(ReadOnlySpan<PBR.PunctualLight> sources)
    {
        if (sources.Length > Capacity) throw new ArgumentOutOfRangeException(nameof(sources));
        Span<System.Numerics.Vector4> payloads = stackalloc System.Numerics.Vector4[sources.Length];
        // Validate the complete generation before mutating its count or any exposed vector.
        for (int i = 0; i < sources.Length; i++) payloads[i] = sources[i].IntensityPayload();
        double red = 0, green = 0, blue = 0;
        foreach (var payload in payloads)
        {
            red += payload.X; green += payload.Y; blue += payload.Z;
        }
        float candelaMaximum = PBR.LightingStorage.UpperBound(Math.Max(red, Math.Max(green, blue)) * (1 + 256.0 / (1 << 23)));
        WriteUInt32(LightCountOffset, (uint)sources.Length);
        var snapshot = ImmutableArray.CreateBuilder<ShadowPointLight>(sources.Length);
        for (int i = 0; i < sources.Length; i++)
        {
            WriteVector4(PositionsOffset + i * VectorStride, new(sources[i].Position, 0));
            WriteVector4(ColorsOffset + i * VectorStride, payloads[i]);
            snapshot.Add(new(sources[i].Source, sources[i].Position, payloads[i], null));
        }
        SummedCandelaMaximum = candelaMaximum;
        // Authored candela carry a different payload convention and do not certify
        // the engine's legacy RGB/range expressions used by clouds and particles.
        NativeEffectBounds = default;
        Lights = snapshot.MoveToImmutable();
    }
    #endregion
}
