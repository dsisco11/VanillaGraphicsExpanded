using System;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Captures the ordinary native cloud owner's geometry and color envelope before its later tile commit.</summary>
internal readonly record struct SceneColorCloudInputs(uint FrameIndex, CloudRenderer? Owner,
    float CloudsLength, float PositionRadius, float GlobalBrightnessMagnitude, float VertexMaximum)
{
    private static readonly AccessTools.FieldRef<WeatherSystemClient, CloudRenderer> Renderer =
        AccessTools.FieldRefAccess<WeatherSystemClient, CloudRenderer>("cloudRenderer");
    private static readonly AccessTools.FieldRef<CloudRenderer, float> Brightness =
        AccessTools.FieldRefAccess<CloudRenderer, float>("blendedGlobalCloudBrightness");
    private static readonly AccessTools.FieldRef<CloudRenderer, bool> Enabled =
        AccessTools.FieldRefAccess<CloudRenderer, bool>("renderClouds");

    #region Public API
    /// <summary>Reads the actual native owner and covers retained brightness and later tile rebuilds.</summary>
    internal static SceneColorCloudInputs FromNative(ICoreClientAPI api, float temporaryPerceptionMultiplier = 1,
        float? capturedPerceptionIntensity = null)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (!float.IsFinite(temporaryPerceptionMultiplier) || temporaryPerceptionMultiplier <= 0)
            throw new ArgumentOutOfRangeException(nameof(temporaryPerceptionMultiplier));
        uint frame = VgeFrameRenderer.Current.FrameIndex;
        var weather = api.ModLoader.GetModSystem<WeatherSystemClient>();
        var renderer = weather is null ? null : Renderer(weather);
        if (renderer is null || !Enabled(renderer)) return new(frame, renderer, 0, 0, 0, 0);
        var calendar = api.World.Calendar;
        var uniforms = api.Render.ShaderUniforms;
        float perception = uniforms.PerceptionEffectIntensity;
        if (capturedPerceptionIntensity is { } captured)
        {
            // Compare the exact native temporary multiplication instead of dividing
            // it back, which can round one ULP above the original admitted input.
            if (!float.IsFinite(captured) || perception != captured * temporaryPerceptionMultiplier)
                throw new InvalidOperationException("Native cloud perception changed after frame admission.");
            perception = captured;
        }
        else perception /= temporaryPerceptionMultiplier;
        // CloudTick can commit a view-distance rebuild after this capture. Its owner
        // clamps the rebuilt tile count to at least twenty. The smallest retained or
        // supported upcoming length bounds the unclamped signed edge amplification;
        // larger lengths cannot increase the absolute color envelope.
        int minimumTileLength = Math.Min(renderer.CloudTileLength, 20);
        var inputs = FromInputs(frame, renderer.CloudTileSize, minimumTileLength,
            calendar.DayLightStrength, calendar.MoonLightStrength, Brightness(renderer),
            api.Ambient.BlendedCloudBrightness, uniforms.PerceptionEffectId,
            perception);
        return inputs with { Owner = renderer };
    }

    /// <summary>Bounds normalized short instance attributes and native cube geometry without scanning changing tiles.</summary>
    internal static SceneColorCloudInputs FromInputs(uint frame, int tileSize, int tileLength,
        float dayLight, float moonLight, float retainedBrightness, float upcomingBrightness,
        int perceptionEffect, float perceptionIntensity)
    {
        if (tileSize <= 0 || tileLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(tileSize), "Native cloud geometry requires positive tile dimensions.");
        Validate(dayLight); Validate(moonLight); Validate(retainedBrightness);
        Validate(upcomingBrightness); Validate(perceptionIntensity);
        const double guard = 1 + 256.0 / (1 << 23);
        // The owner temporarily multiplies vertex perception intensity by twenty.
        // Fragment psychedelic strength is a distinct input and is not multiplied here.
        if (Math.Abs((double)perceptionIntensity) * 20 * guard > float.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(perceptionIntensity));
        double warp = perceptionEffect == 2 ? 72 * Math.Max(0, (double)perceptionIntensity) : 0;
        // LoadCloudModel uses half/quarter integer tile extents. Normalized short
        // offsets are multiplied by 32767 in the shader, not by the tile size.
        // Signed thickness/thinness endpoints add at most 1.21*1000*4=4840 vertically.
        double horizontal = 32767 + (double)(tileSize / 2) + warp;
        double vertical = tileSize / 4 + warp + 4840;
        float radius = LightingStorage.UpperBound(Math.Sqrt(2 * horizontal * horizontal + vertical * vertical) * guard);
        float length = (float)tileSize * tileLength;
        float daylight = Math.Max(0, dayLight - .95f * moonLight);
        float brightness = Math.Max(Math.Abs(retainedBrightness), Math.Abs(upcomingBrightness));
        // Every possible committed tile retains the same normalized attribute domain;
        // the later CloudTick may replace geometry and copy the captured ambient gain.
        float vertexMaximum = SceneColorCompatibilityBounds.CloudVertexColor(daylight, 1, brightness, 1, radius, length);
        return new(frame, null, length, radius, brightness, vertexMaximum);
    }
    #endregion

    #region Private
    /// <summary>Rejects nonfinite source inputs before publishing derived geometry or color metadata.</summary>
    private static void Validate(float value)
    {
        if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(value));
    }
    #endregion
}
