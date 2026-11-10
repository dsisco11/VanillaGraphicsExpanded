using System;
using HarmonyLib;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Separates native geometric detail selection from camera culling and shadow-page resolution.</summary>
internal readonly record struct NativeShadowTerrainDetail(double ViewX, double ViewZ, double FineDistanceSquared, double CoarseDistanceSquared)
{
    private static readonly AccessTools.FieldRef<FrustumCulling, float> Fine =
        AccessTools.FieldRefAccess<FrustumCulling, float>("lod0BiasSq");
    private static readonly AccessTools.FieldRef<FrustumCulling, double> Coarse =
        AccessTools.FieldRefAccess<FrustumCulling, double>("lod2BiasSq");

    #region Public API
    /// <summary>Captures the native detail thresholds without invoking its stateful camera culling routine.</summary>
    internal static NativeShadowTerrainDetail Capture(FrustumCulling culler, double viewX, double viewZ)
    {
        var result = new NativeShadowTerrainDetail(viewX, viewZ, Fine(culler), Coarse(culler));
        if (!double.IsFinite(viewX) || !double.IsFinite(viewZ) || !double.IsFinite(result.FineDistanceSquared)
            || !double.IsFinite(result.CoarseDistanceSquared) || result.CoarseDistanceSquared < 0)
            throw new InvalidOperationException("Native terrain detail thresholds are unavailable.");
        return result;
    }

    /// <summary>Preserves authored detail alternatives while retaining offscreen and beyond-view-distance casters.</summary>
    internal bool Eligible(ModelDataPoolLocation range)
    {
        if (range.Hide) return false;
        var sphere = range.FrustumCullSphere;
        if (!float.IsFinite(sphere.x) || !float.IsFinite(sphere.z))
            throw new InvalidOperationException("Native detail selection has no finite range center.");
        double x = ViewX - sphere.x, z = ViewZ - sphere.z, distance = x * x + z * z;
        // The engine's distance cutoff is camera visibility, not geometry detail. Keep
        // the complementary detail 2/3 selection beyond that cutoff inside the loaded envelope.
        return range.LodLevel switch
        {
            0 => FineDistanceSquared > 0 && distance < FineDistanceSquared + 1024,
            1 => true,
            2 => distance <= CoarseDistanceSquared,
            3 => distance > CoarseDistanceSquared,
            _ => throw new NotSupportedException("Unknown native terrain detail category.")
        };
    }
    #endregion
}
