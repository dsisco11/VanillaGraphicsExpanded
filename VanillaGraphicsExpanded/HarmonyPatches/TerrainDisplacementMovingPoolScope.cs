using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Excludes transformed mini-dimensions until their shared displacement metric is supported.</summary>
[HarmonyPatch(typeof(MeshDataPool), nameof(MeshDataPool.RenderMesh))]
internal static class TerrainDisplacementMovingPoolScope
{
    #region Engine callbacks
    /// <summary>Uses engine pool dimension provenance rather than inferring transforms from shader matrices.</summary>
    [HarmonyPrefix]
    internal static void Prefix(int ___dimensionId, out bool __state)
    {
        __state = TerrainDisplacementRuntime.MovingPool;
        TerrainDisplacementRuntime.MovingPool = ___dimensionId == Vintagestory.API.Config.Dimensions.MiniDimensions;
    }

    /// <summary>Restores scope on every draw exit.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(bool __state) => TerrainDisplacementRuntime.MovingPool = __state;
    #endregion
}
