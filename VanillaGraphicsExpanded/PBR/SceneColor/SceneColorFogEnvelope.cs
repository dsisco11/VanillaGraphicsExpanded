using System;
using System.Numerics;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Retains the current frame's actual authored fog color endpoints, independently of geometric fog validity.</summary>
internal readonly record struct SceneColorFogEnvelope(uint FrameIndex, float AuthoredMaximum, float MaximumNits)
{
    private static readonly AccessTools.FieldRef<ClientMain, Vintagestory.API.MathTools.Vec3f> SkyFog =
        AccessTools.FieldRefAccess<ClientMain, Vintagestory.API.MathTools.Vec3f>("FogColorSky");

    #region Public API
    /// <summary>Captures existing native fog owners without querying rendered pixels or changing their colors.</summary>
    internal static SceneColorFogEnvelope FromNative(ICoreClientAPI api)
    {
        ArgumentNullException.ThrowIfNull(api);
        if (api.World is not ClientMain game)
            throw new InvalidOperationException("Native fog requires the active client world owner.");
        var render = api.Render;
        var standard = render.FogColor;
        var ambient = api.Ambient.BlendedFogColor;
        var sky = SkyFog(game);
        var uniforms = render.ShaderUniforms;
        return FromInputs(VgeFrameRenderer.Current.FrameIndex, new(standard.X, standard.Y, standard.Z),
            new(ambient.R, ambient.G, ambient.B), new(sky.R, sky.G, sky.B),
            uniforms.FogSphereQuantity, uniforms.FogSpheres);
    }

    /// <summary>Bounds convex fog color interpolation across the concrete native endpoints and active sphere colors.</summary>
    internal static SceneColorFogEnvelope FromInputs(uint frameIndex, Vector3 standard, Vector3 ambient,
        Vector3 sky, int sphereQuantity, ReadOnlySpan<float> spheres)
    {
        if (sphereQuantity is < 0 or > 3 || spheres.Length < sphereQuantity * 8)
            throw new ArgumentException("Native sphere fog must match its three-sphere shader storage.", nameof(sphereQuantity));
        float maximum = Math.Max(Peak(standard), Math.Max(Peak(ambient), Peak(sky)));
        for (int sphere = 0; sphere < sphereQuantity; sphere++)
        {
            int offset = sphere * 8;
            // Coordinates/radius/density are not lighting quantities. Reject nonfinite
            // inputs here, but leave geometric intersection admission to its draw owner.
            for (int field = 0; field < 5; field++)
                if (!float.IsFinite(spheres[offset + field]))
                    throw new ArgumentException("Sphere fog inputs must be finite.", nameof(spheres));
            maximum = Math.Max(maximum, Peak(new(spheres[offset + 5], spheres[offset + 6], spheres[offset + 7])));
        }
        // The native sphere mix clamps its factor. Repeated spheres therefore use
        // the maximum endpoint, not a sum or a second physical-light contribution.
        return new(frameIndex, maximum, SceneColorCompatibilityBounds.Fog(maximum));
    }
    #endregion

    #region Private
    /// <summary>Validates authored RGB before extracting its actual component peak.</summary>
    private static float Peak(Vector3 color)
    {
        if (!LightingCalibration.FiniteNonnegative(color))
            throw new ArgumentException("Fog color requires finite nonnegative authored RGB.", nameof(color));
        return Math.Max(color.X, Math.Max(color.Y, color.Z));
    }
    #endregion
}
