using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Withdraws borrows at the engine's actual pool retirement boundary.</summary>
[HarmonyPatch(typeof(MeshDataPool), nameof(MeshDataPool.Dispose))]
internal static class LiquidGeometryPoolRetirementHook
{
    #region Public API
    /// <summary>Expires borrowers before engine mesh deletion can reuse native names.</summary>
    internal static void Prefix(MeshDataPool __instance) => LiquidGeometryLifecycle.Retire(__instance);
    #endregion
}

