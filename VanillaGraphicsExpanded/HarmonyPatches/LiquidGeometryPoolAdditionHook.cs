using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Detects pool additions at the owning engine upload boundary.</summary>
[HarmonyPatch(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.AddModel))]
internal static class LiquidGeometryPoolAdditionHook
{
    #region Public API
    /// <summary>Captures only the original collection length for this operation.</summary>
    internal static void Prefix(List<MeshDataPool> ___pools, out int __state) => __state = ___pools.Count;

    /// <summary>Signals appended storage even when model insertion itself fails.</summary>
    internal static void Finalizer(List<MeshDataPool> ___pools, int __state)
    {
        if (___pools.Count != __state) LiquidGeometryLifecycle.ChangedCollections();
    }
    #endregion
}

