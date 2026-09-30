using System;
using System.Numerics;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Captures one world-anchored wave state for both liquid render passes.</summary>
internal readonly record struct LiquidWaveFrame(Vector4 Phases, float Wind)
{
    private const double Gravity = 9.80665;
    private const double WaterDepthMetres = 3.0;
    private const double TwoPi = Math.PI * 2.0;

    #region Capture
    /// <summary>Converts elapsed seconds into wrapped deep-water phases without float-time drift.</summary>
    internal static LiquidWaveFrame Capture(ICoreClientAPI api)
    {
        double seconds = api.World.ElapsedMilliseconds / 1000.0;
        var camera = api.World.Player.Entity.CameraPos;
        float wind = Math.Clamp(api.Render.ShaderUniforms.WindWaveIntensity, 0, 1);
        return FromState(seconds, camera.X, camera.Z, wind);
    }

    /// <summary>Builds all band phases from one SI-unit time and absolute camera position.</summary>
    internal static LiquidWaveFrame FromState(double seconds, double cameraXMetres, double cameraZMetres, float wind)
    {
        return new(new(Phase(seconds, cameraXMetres, cameraZMetres, 3, 0.89442719, 0.44721360),
            Phase(seconds, cameraXMetres, cameraZMetres, 5, -0.31622777, 0.94868330),
            Phase(seconds, cameraXMetres, cameraZMetres, 8, 0.70710678, -0.70710678),
            Phase(seconds, cameraXMetres, cameraZMetres, 13, -0.85749293, -0.51449576)), Math.Clamp(wind, 0, 1));
    }

    /// <summary>Folds elapsed time and camera metres into one periodic world-anchored phase.</summary>
    private static float Phase(double seconds, double cameraX, double cameraZ,
        double wavelengthMetres, double directionX, double directionZ)
    {
        double k = TwoPi / wavelengthMetres;
        double omega = Math.Sqrt(Gravity * k * Math.Tanh(k * WaterDepthMetres));
        return (float)(((seconds * omega - k * (cameraX * directionX + cameraZ * directionZ)) % TwoPi + TwoPi) % TwoPi);
    }
    #endregion
}
