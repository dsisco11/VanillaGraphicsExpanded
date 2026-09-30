using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Labels uploaded and reserved engine meshes after their GL objects are initialized.</summary>
[HarmonyPatch]
internal static class EngineMeshDebugLabelsHook
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

    /// <summary>Covers ordinary uploads and both pooled allocation layouts.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ClientPlatformWindows), "UploadMesh");
        yield return AccessTools.Method(typeof(ClientPlatformWindows), "AllocateEmptyMesh");
        yield return AccessTools.Method(typeof(ClientPlatformWindows), "AllocateEmptySSBOMesh");
    }

    /// <summary>Applies stream names through the borrowed engine mesh interface.</summary>
    [HarmonyPostfix]
    internal static void Postfix(MeshRef __result)
    {
        EngineMeshDebugLabels.Apply(__result);
    }
    #endregion
}
