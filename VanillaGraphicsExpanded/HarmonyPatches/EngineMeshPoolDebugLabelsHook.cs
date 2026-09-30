using System.Collections.Generic;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Diagnostics;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Adds semantic pool names only when an engine upload grows its pool list.</summary>
[HarmonyPatch(typeof(MeshDataPoolManager), nameof(MeshDataPoolManager.AddModel))]
internal static class EngineMeshPoolDebugLabelsHook
{
    #region Public API
    /// <summary>Installs resource instrumentation only in Debug builds, matching GlDebug labeling.</summary>
    [HarmonyPrepare]
    internal static bool Prepare()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>Captures the constant-time allocation boundary without scanning existing mesh pools.</summary>
    [HarmonyPrefix]
    internal static void Prefix(List<MeshDataPool> ___pools, out int __state)
    {
        __state = ___pools.Count;
    }

    /// <summary>Names new allocations after the engine has published their complete buffer references.</summary>
    [HarmonyPostfix]
    internal static void Postfix(MeshDataPoolManager __instance, List<MeshDataPool> ___pools, int __state)
    {
        EngineMeshPoolDebugLabels.LabelAdded(__instance, ___pools, __state);
    }
    #endregion
}
