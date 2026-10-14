using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Sequences celestial transport through one backend and one retained medium table.</summary>
internal sealed partial class AtmosphereBackend
{
    private CelestialRequest? celestialRequest, completedCelestial, cachedSunRequest, cachedMoonRequest;
    private AtmosphereLighting? solarTransport, cachedSun, cachedMoon;
    private CelestialRequest? combinedRequest;
    private AtmosphereLighting? combinedTransport;
    private Task<AtmosphereLighting>? diffusePending;
    private CancellationTokenSource diffuseCancellation = new();

    /// <summary>Freezes both emitters and the shared medium until their complete publication is ready.</summary>
    private readonly record struct CelestialRequest(AtmosphereCelestialInputs Sources, float Altitude, float Clouds,
        int Width, int Height, int Quality, float Albedo, CelestialSource? Source, Matrix4x4 Rotation)
    {
        internal (((Vector3, Vector3, int), float, float, int, int, int, int), CelestialSource?, Matrix4x4) Key =>
            ((Sources.Key, MathF.Round(Altitude * 40), MathF.Round(Clouds * 20),
            Width, Height, Quality, AtmosphereSeasonModel.AlbedoBucket(Albedo)), Source, Rotation);

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
        int width, int height, int quality, float groundAlbedo, CelestialSource? source = null, Matrix4x4? rotation = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (diffusePending is { } pending)
        {
            if (!pending.IsCompleted) return null;
            diffusePending = null;
            try
            {
                var result = pending.GetAwaiter().GetResult();
                completedCelestial = celestialRequest; celestialRequest = null; solarTransport = null;
                return result;
            }
            catch { celestialRequest = null; solarTransport = null; throw; }
        }
        sources = sources.Normalize();
        if (!float.IsFinite(altitude) || !float.IsFinite(clouds) || !float.IsFinite(groundAlbedo))
            throw new ArgumentException("Celestial transport requires finite medium inputs.");
        // Reused directional results must carry exactly the same medium geometry,
        // not different raw observer heights that merely share an admission bucket.
        float admittedAltitude = MathF.Round(Math.Clamp(altitude, 0, 99) * 40) / 40;
        float admittedClouds = MathF.Round(Math.Clamp(clouds, 0, 1) * 20) / 20;
        var requested = new CelestialRequest(sources, admittedAltitude, admittedClouds,
            Math.Clamp(width, 16, 128), Math.Clamp(height, 8, 96), Math.Clamp(quality, 0, 3), groundAlbedo,
            source, rotation ?? Matrix4x4.Identity);
        if (celestialRequest is null)
        {
            if (completedCelestial is { } completed && completed.Key.Equals(requested.Key)
                && ReferenceEquals(completed.Source, requested.Source)) return null;
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
        // Rotation-only refreshes reuse the solar/lunar/airglow arrays as well as directional transport.
        // Source-derived aerial additions are then produced off-thread from this immutable base.
        var ready = combinedTransport is not null && combinedRequest is { } previous
            && previous.Key.Item1 == admitted.Key.Item1 ? combinedTransport
            : AtmosphereNightLighting.Combine(solarTransport, lunar, admitted.Sources, admitted.Clouds);
        combinedTransport = ready; combinedRequest = admitted;
        if (admitted.Source is { } external)
        {
            // Reuse the completed transport's extinction on a worker; never run another atmospheric integrator.
            var token = diffuseCancellation.Token;
            diffusePending = Task.Run(() => CelestialDiffuseLighting.Apply(ready, external, admitted.Rotation, token), token);
            _ = diffusePending.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return null;
        }
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
        diffuseCancellation.Cancel(); diffuseCancellation.Dispose();
        diffuseCancellation = new(); diffusePending = null;
        celestialRequest = completedCelestial = cachedSunRequest = cachedMoonRequest = null;
        solarTransport = cachedSun = cachedMoon = null;
        combinedTransport = null; combinedRequest = null;
    }
    #endregion
}
