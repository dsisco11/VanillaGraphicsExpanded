using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Retains pool provenance while the engine reuses main and shadow executables.</summary>
[HarmonyPatch]
internal static class TerrainDisplacementRenderScope
{
    #region Engine callbacks
    /// <summary>Retains identical pool eligibility in main and shadow rendering, including shared opaque shader draws.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.RenderShadow));
        yield return AccessTools.Method(typeof(ChunkRenderer), nameof(ChunkRenderer.RenderOpaque));
    }
    /// <summary>Captures the engine pool table only for the duration of terrain rendering.</summary>
    [HarmonyPrefix]
    internal static void Prefix(MeshDataPoolManager[][] ___poolsByRenderPass, out MeshDataPoolManager[][]? __state)
    {
        __state = TerrainDisplacementRuntime.TerrainPools;
        TerrainDisplacementRuntime.TerrainPools = ___poolsByRenderPass;
    }

    /// <summary>Clears provenance even when rendering throws.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(MeshDataPoolManager[][]? __state) => TerrainDisplacementRuntime.TerrainPools = __state;
    #endregion
}

/// <summary>Scopes eligible main and shadow draws to opaque and topsoil managers.</summary>
[HarmonyPatch(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.Render))]
internal static class TerrainDisplacementManagerScope
{
    #region Engine callbacks
    /// <summary>Saves nesting state and resolves manager identity without GPU queries.</summary>
    [HarmonyPrefix]
    internal static void Prefix(MeshDataPoolManager __instance, out bool __state)
    {
        __state = TerrainDisplacementRuntime.EligiblePool;
        TerrainDisplacementRuntime.EligiblePool = TerrainDisplacementRuntime.IsEligiblePool(__instance);
    }

    /// <summary>Restores the enclosing manager scope.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(bool __state) => TerrainDisplacementRuntime.EligiblePool = __state;
    #endregion
}

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
