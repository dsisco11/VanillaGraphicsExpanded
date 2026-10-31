using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Invalidates geometry selection when the engine installs a manager into its pass/atlas table.</summary>
[HarmonyPatch(typeof(ChunkRenderer), "AddPoolsForAtlasAndPass")]
internal static class LiquidGeometryManagerHook
{
    #region Public API
    /// <summary>Schedules re-preparation after the engine has published its new manager.</summary>
    internal static void Postfix() => LiquidGeometryLifecycle.ChangedCollections();
    #endregion
}
