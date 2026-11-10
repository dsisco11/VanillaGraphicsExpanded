using System;
using System.Collections.Immutable;
using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Lights;

/// <summary>Freezes the exact light payload and coordinate convention alongside source ownership.</summary>
internal sealed record ShadowLightSnapshot(long Frame, long World, int Dimension,
    ImmutableArray<ShadowPointLight> Points, ShadowDirectionalLight Sun, ShadowDirectionalLight Moon,
    ImmutableArray<double> WorldToView, int OriginX, int OriginY, int OriginZ,
    Vector3 OriginRemainder)
{
    /// <summary>Counts defensively unidentifiable sources without excluding their illumination.</summary>
    internal int UnknownSources
    {
        get { int count = 0; foreach (var light in Points) if (!light.Source.IsKnown) count++; return count; }
    }

    #region Public API
    /// <summary>Uses the shader's uncalibrated native radius or explicit authored radius exactly.</summary>
    internal static float InfluenceRadius(Vector4 payload)
    {
        if (!float.IsFinite(payload.X) || !float.IsFinite(payload.Y) || !float.IsFinite(payload.Z) || !float.IsFinite(payload.W))
            throw new ArgumentException("Light payload must be finite.", nameof(payload));
        return payload.W > 0 ? payload.W : MathF.Min(25, Vector3.Max(new(payload.X, payload.Y, payload.Z), Vector3.Zero).Length());
    }
    #endregion
}
