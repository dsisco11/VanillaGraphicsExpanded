using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Bounds the inspected native scene expressions before their authored-color conversion.</summary>
internal static class SceneColorCompatibilityBounds
{
    #region Public API
    #region Authored fog endpoints
    /// <summary>Converts an admitted authored fog endpoint maximum without treating its RGB as illumination.</summary>
    internal static float Fog(float authoredMaximum)
    {
        Validate(authoredMaximum);
        return Publish(Decode(authoredMaximum));
    }
    #endregion

    #region Prelit particle expressions
    /// <summary>Bounds native cube lighting before shadow, convex fog and underwater blending.</summary>
    internal static float CubeParticles(Vector3 ambient, float nightVision, float minimumBrightness,
        float particleColorMaximum, float shadowIntensity, float fogMaximum, float underwaterTintMaximum,
        SceneColorPointLightBounds pointBounds, float minimumMurk, float maximumMurk)
    {
        if (!LightingCalibration.FiniteNonnegative(ambient) || !pointBounds.Supported)
            throw new ArgumentException("Particle lighting requires admitted native ambient and point sources.");
        Unit(nightVision); Unit(particleColorMaximum); Validate(minimumBrightness);
        Validate(shadowIntensity); Validate(fogMaximum); Validate(underwaterTintMaximum);
        ConvexMurk(minimumMurk, maximumMurk);
        double ambientMean = ((double)ambient.X + ambient.Y + ambient.Z) / 3;
        // Scene particle/decal shaders guard the weighted denominator and normalize
        // chromaticity before restoring brightness. Black ambient is a supported
        // source, including startup and nights without lunar illumination.
        double pointStrength = pointBounds.CloudMaximum > 0
            ? 1 + (25.0 / 15) * Math.Max(25, Math.Max(1, Math.Abs(1 - ambientMean))) : 0;
        double brightness = Math.Max(1, Math.Max(minimumBrightness,
            Math.Max(ambientMean + nightVision, pointStrength + .45 * nightVision)));
        const double rounding = 256.0 / (1 << 23);
        double denominatorMaximum = 2 * (ambientMean + nightVision) + Math.Max(1, minimumBrightness)
            + pointStrength + .45 * nightVision;
        double ambientPeak = Math.Max(ambient.X, Math.Max(ambient.Y, ambient.Z));
        // Certify sums/products separately; dark inputs no longer require a
        // positive ambient-only lower bound for the final brightness reciprocal.
        double numeratorMaximum = 2 * (ambientMean + nightVision) * Math.Max(ambientPeak, .5)
            + Math.Max(1, minimumBrightness) + (pointStrength + .45 * nightVision) * (1 + .5 * nightVision);
        if (denominatorMaximum * (1 + rounding) > float.MaxValue
            || numeratorMaximum * (1 + rounding) > float.MaxValue
            || 3 * brightness * (1 + rounding) > float.MaxValue)
            throw new ArgumentException("Native particle RGB normalization exceeds its finite arithmetic domain.");
        // Nonnegative RGB normalized to mean bMax has component maximum 3*bMax.
        // Glow/contrast add at most 1.25*1.05, and shadow RGB has its separate gain.
        double shadowGain = 1 + Math.Max(0, 2.0 * shadowIntensity - 1.66) / 1.5;
        double authored = Math.Max(3.9375 * brightness * particleColorMaximum * shadowGain, fogMaximum);
        return Publish(Decode(Math.Max(authored, .4 * underwaterTintMaximum)));
    }
    #endregion

    #region Owned physical sky
    /// <summary>Bounds only authored sky additions; physical atmosphere bypasses sRGB decoding and retains its own bound.</summary>
    internal static float PhysicalSky(float perceptionStrength, float underwaterTintMaximum, float nightVision)
    {
        Validate(perceptionStrength); Validate(underwaterTintMaximum); Validate(nightVision);
        // Perception works in reference-nit units. Tint is a convex endpoint;
        // adding its whole endpoint is conservative without recoding the atmosphere.
        return Publish(LightingCalibration.EmissionReferenceNits * 1.6 * perceptionStrength
            + Decode(.4 * underwaterTintMaximum) + Decode(.5 * nightVision));
    }
    #endregion

    #region Celestial expressions
    /// <summary>Includes native star sampling, night vision, underwater tint and its actual extrapolating alpha.</summary>
    internal static float NightSky(float textureMaximum, float nightVision, float underwaterTintMaximum,
        float dayLight, float backgroundMaximumNits, float minimumMurk, float maximumMurk)
    {
        Validate(textureMaximum); Validate(nightVision); Validate(underwaterTintMaximum);
        Validate(dayLight); Validate(backgroundMaximumNits);
        ConvexMurk(minimumMurk, maximumMurk);
        double authored = Math.Max(Math.Max(0, 2.0 * (textureMaximum - .03)) + .5 * nightVision,
            .4 * underwaterTintMaximum);
        double alpha = Math.Max(0, 1.0 - 2.0 * (dayLight - .05));
        // Native alpha reaches 1.1 at night. Budget its actual source multiplier and
        // any amplification beyond the already included background; do not clamp it.
        double additional = alpha * Decode(authored)
            + Math.Max(0, Math.Abs(1 - alpha) - 1) * backgroundMaximumNits;
        return Publish(additional);
    }

    /// <summary>Bounds native phase multiplication, restored fog, nonlinear lunar brightness and sky/underwater endpoints.</summary>
    internal static float Moon(float textureColorMaximum, float alphaMaximum, float fogMaximum,
        float skyMaximum, float underwaterTintMaximum, bool nonlinearBrightness, float minimumMurk, float maximumMurk)
    {
        Validate(textureColorMaximum); Unit(alphaMaximum); Validate(fogMaximum);
        Validate(skyMaximum); Validate(underwaterTintMaximum);
        ConvexMurk(minimumMurk, maximumMurk);
        double phase = Math.Max(textureColorMaximum * (alphaMaximum + .05), fogMaximum);
        // Fog is applied after the phase multiplier and can restore a brighter
        // endpoint. The nonlinear branch scales that restored RGB, not just the texture.
        double brightness = nonlinearBrightness ? phase * Math.Pow(3 * phase / 2.8, 1.4) : phase;
        return Publish(Decode(Math.Max(Math.Max(brightness, skyMaximum), .4 * underwaterTintMaximum)));
    }
    #endregion

    #region Cloud expressions
    /// <summary>Bounds normalized-byte native vertex RGB using its admitted radius before undulation displacement.</summary>
    internal static float CloudVertexColor(float dayLight, float tileBrightnessMagnitude,
        float globalBrightnessMagnitude, float undulationMagnitude, float positionRadius, float cloudsLength)
    {
        Validate(dayLight); Unit(tileBrightnessMagnitude); Validate(globalBrightnessMagnitude);
        Unit(undulationMagnitude); Validate(positionRadius); Validate(cloudsLength);
        if (cloudsLength == 0) throw new ArgumentOutOfRangeException(nameof(cloudsLength));
        // The native edge factor uses min(1, edge), without a lower clamp. Distant
        // positions can therefore amplify signed undulation rather than fade it.
        // Radius includes tile offsets, perception warp and thickness displacement;
        // the later undulation/curvature displacement does not enter this expression.
        const double guard = 1 + 256.0 / (1 << 23);
        double edgeNumeratorMinimum = .5 * cloudsLength - positionRadius + 400;
        double edgeNumeratorMaximum = .5 * cloudsLength + 400;
        double edgeNumeratorMagnitude = Math.Max(Math.Abs(edgeNumeratorMinimum), edgeNumeratorMaximum);
        // Certify the native squared norm and division before min: clipping an
        // overflowing positive edge to one does not admit the original arithmetic.
        // The fade denominator is also evaluated before the final alpha clamp.
        if (positionRadius * (double)positionRadius * guard > float.MaxValue
            || edgeNumeratorMagnitude * guard > float.MaxValue
            || edgeNumeratorMagnitude / cloudsLength * guard > float.MaxValue
            || cloudsLength * .2 < 1.1754943508222875e-38)
            throw new ArgumentOutOfRangeException(nameof(cloudsLength), "Native cloud geometry arithmetic is unsupported.");
        double edgeMinimum = edgeNumeratorMinimum / cloudsLength;
        double edgeMagnitude = Math.Max(1, Math.Abs(Math.Min(1, edgeMinimum)));
        // mix(1, .2 + .75*(noise*u*edge + .5), u)
        // = 1 - .425*u + .75*noise*edge*u*u, with |noise| <= 3.6.
        double modulation = 1 + .425 * undulationMagnitude
            + 2.7 * edgeMagnitude * undulationMagnitude * undulationMagnitude;
        double authored = dayLight * (double)tileBrightnessMagnitude * globalBrightnessMagnitude * modulation;
        // Certify vertex arithmetic independently of the later sRGB conversion.
        // An unrepresentable intermediate must not be made valid by a zero gain.
        if (edgeMagnitude * 3.6 * 50 * guard > float.MaxValue
            || modulation * guard > float.MaxValue
            || dayLight * (double)tileBrightnessMagnitude * globalBrightnessMagnitude * guard > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(cloudsLength), "Native cloud vertex arithmetic is unsupported.");
        return LightingStorage.UpperBound(authored * guard);
    }

    /// <summary>Includes both glow multipliers, perception, fog, actual adjusted point colors and underwater tint.</summary>
    internal static float Clouds(float vertexMaximum, float glowFogMaximum, float perceptionStrength,
        float fogMaximum, float adjustedPointMaximum, float nightVision, float underwaterTintMaximum,
        float minimumMurk, float maximumMurk)
    {
        Validate(vertexMaximum); Validate(glowFogMaximum); Validate(perceptionStrength);
        Validate(fogMaximum); Validate(adjustedPointMaximum); Validate(nightVision); Validate(underwaterTintMaximum);
        ConvexMurk(minimumMurk, maximumMurk);
        // With admitted unit glow coverage, the second native multiplier is at
        // most one. Perception precedes fog; point and night terms follow it.
        double glowColor = vertexMaximum * Math.Max(1, 1.2 * glowFogMaximum);
        // Vertex modulation may be signed. Native perception only caps the RGB
        // sum above at one, so a negative sum amplifies its palette contribution.
        // Use the absolute RGB envelope until the authored-color adapter floors it.
        double perceptionBrightness = Math.Max(1, 3 * glowColor);
        double authored = Math.Max(glowColor + 1.6 * perceptionBrightness * perceptionStrength,
            fogMaximum) + adjustedPointMaximum + .5 * nightVision;
        return Publish(Decode(Math.Max(authored, .4 * underwaterTintMaximum)));
    }

    /// <summary>Bounds the native cloud map's RGB with signed normalized tile attributes and its retained brightness gain.</summary>
    internal static float CloudMap(float tileBrightnessMagnitude, float undulationMagnitude, float globalBrightnessMagnitude,
        float glowFogMaximum, float fogMaximum, float adjustedPointMaximum, float nightVision)
    {
        Unit(tileBrightnessMagnitude); Unit(undulationMagnitude); Validate(globalBrightnessMagnitude);
        Validate(glowFogMaximum); Validate(fogMaximum); Validate(adjustedPointMaximum); Validate(nightVision);
        // mix(1, .65 + .5*gnoise*u, u) = 1 - .35*u + .5*gnoise*u*u.
        // The native gnoise bound is 3.6; retain signed normalized endpoints.
        double undulation = 1 + .35 * undulationMagnitude + 1.8 * undulationMagnitude * undulationMagnitude;
        double grey = (.1 + .7 * tileBrightnessMagnitude) * globalBrightnessMagnitude * undulation;
        double authored = Math.Max(grey * Math.Max(1, 1.2 * glowFogMaximum), fogMaximum)
            + .7 * adjustedPointMaximum + .5 * nightVision;
        // The matching map generation has alpha one. Positive traversal weights
        // preserve this RGB/alpha bound through all three normalized OIT buckets.
        return Publish(Decode(authored));
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Matches the monotone authored-color shader adapter, including values above one.</summary>
    private static double Decode(double authored)
    {
        if (!double.IsFinite(authored) || authored < 0) throw new ArgumentOutOfRangeException(nameof(authored));
        return LightingCalibration.EmissionReferenceNits * (authored <= .04045
            ? authored / 12.92 : Math.Pow((authored + .055) / 1.055, 2.4));
    }

    /// <summary>Publishes outward metadata with separate headroom for the inspected Float32 expression and decode.</summary>
    private static float Publish(double nits) => LightingStorage.UpperBound(nits * (1 + 256.0 / (1 << 23)));

    /// <summary>Rejects unsupported source domains instead of substituting a valid black source.</summary>
    private static void Validate(float value)
    {
        if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Admits normalized coverage or a normalized signed attribute magnitude without changing its value.</summary>
    private static void Unit(float value)
    {
        Validate(value);
        if (value > 1) throw new ArgumentOutOfRangeException(nameof(value));
    }

    /// <summary>Requires the owner to prove convex native underwater mixing; the native shader does not clamp it.</summary>
    private static void ConvexMurk(float minimum, float maximum)
    {
        Unit(minimum); Unit(maximum);
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
    }
    #endregion
}
