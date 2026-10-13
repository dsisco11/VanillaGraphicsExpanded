using System;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Sequences celestial transport through one backend and one retained medium table.</summary>
internal sealed partial class AtmosphereBackend
{
    private CelestialRequest? celestialRequest, completedCelestial, cachedSunRequest, cachedMoonRequest;
    private AtmosphereLighting? solarTransport, cachedSun, cachedMoon;

    /// <summary>Freezes both emitters and the shared medium until their complete publication is ready.</summary>
    private readonly record struct CelestialRequest(AtmosphereCelestialInputs Sources, float Altitude, float Clouds,
        int Width, int Height, int Quality, float Albedo)
    {
        internal ((Vector3, Vector3, int), float, float, int, int, int, int) Key => (Sources.Key, MathF.Round(Altitude * 40), MathF.Round(Clouds * 20),
            Width, Height, Quality, AtmosphereSeasonModel.AlbedoBucket(Albedo));

        /// <summary>Checks the actual transport inputs independently of lunar phase and the other emitter.</summary>
        internal bool SameTransport(CelestialRequest other, bool moon, bool otherMoon) =>
            (moon ? Sources.Key.Moon : Sources.Key.Sun) == (otherMoon ? other.Sources.Key.Moon : other.Sources.Key.Sun)
            && MathF.Round(Altitude * 40) == MathF.Round(other.Altitude * 40)
            && MathF.Round(Clouds * 20) == MathF.Round(other.Clouds * 20)
            && Width == other.Width && Height == other.Height && Quality == other.Quality
            && AtmosphereSeasonModel.AlbedoBucket(Albedo) == AtmosphereSeasonModel.AlbedoBucket(other.Albedo);
    }

    #region Public API
    /// <summary>Retains the previous complete sky while evaluating both directions without duplicate medium storage.</summary>
    internal AtmosphereLighting? Update(AtmosphereCelestialInputs sources, float altitude, float clouds,
        int width, int height, int quality, float groundAlbedo)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        sources = sources.Normalize();
        if (!float.IsFinite(altitude) || !float.IsFinite(clouds) || !float.IsFinite(groundAlbedo))
            throw new ArgumentException("Celestial transport requires finite medium inputs.");
        // Reused directional results must carry exactly the same medium geometry,
        // not different raw observer heights that merely share an admission bucket.
        float admittedAltitude = MathF.Round(Math.Clamp(altitude, 0, 99) * 40) / 40;
        float admittedClouds = MathF.Round(Math.Clamp(clouds, 0, 1) * 20) / 20;
        var requested = new CelestialRequest(sources, admittedAltitude, admittedClouds,
            Math.Clamp(width, 16, 128), Math.Clamp(height, 8, 96), Math.Clamp(quality, 0, 3), groundAlbedo);
        if (celestialRequest is null)
        {
            if (completedCelestial is { } completed && completed.Key == requested.Key) return null;
            celestialRequest = requested;
        }
        var admitted = celestialRequest.Value;
        // Do not let changing weather or celestial motion starve an admitted pair.
        if (solarTransport is null)
        {
            solarTransport = Transport(admitted, moon: false);
            if (solarTransport is null) return null;
        }
        AtmosphereLighting? lunar = AtmosphereNightLighting.PhaseFraction(admitted.Sources.Phase) == 0
            ? solarTransport : Transport(admitted, moon: true);
        if (lunar is null) return null;
        var ready = AtmosphereNightLighting.Combine(solarTransport, lunar, admitted.Sources, admitted.Clouds);
        completedCelestial = admitted;
        celestialRequest = null;
        solarTransport = null;
        return ready;
    }
    #endregion

    #region Private
    /// <summary>Reuses either completed direction before admitting work to the same GPU/CPU transport owner.</summary>
    private AtmosphereLighting? Transport(CelestialRequest request, bool moon)
    {
        if (cachedSun is not null && cachedSunRequest is { } previousSun && request.SameTransport(previousSun, moon, false))
            return cachedSun;
        if (cachedMoon is not null && cachedMoonRequest is { } previousMoon && request.SameTransport(previousMoon, moon, true))
            return cachedMoon;
        var result = Update(moon ? request.Sources.Moon : request.Sources.Sun, request.Altitude, request.Clouds,
            request.Width, request.Height, request.Quality, request.Albedo);
        if (result is null) return null;
        if (moon) { cachedMoon = result; cachedMoonRequest = request; }
        else { cachedSun = result; cachedSunRequest = request; }
        return result;
    }

    /// <summary>Withdraws partial celestial pairs and their admission caches along with backend reload.</summary>
    private void ResetCelestial()
    {
        celestialRequest = completedCelestial = cachedSunRequest = cachedMoonRequest = null;
        solarTransport = cachedSun = cachedMoon = null;
    }
    #endregion
}
