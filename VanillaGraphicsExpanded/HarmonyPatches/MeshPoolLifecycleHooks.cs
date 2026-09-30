using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Refreshes shared mesh-pool classification only when the engine creates or extends manager tables.</summary>
[HarmonyPatch]
internal static class MeshPoolLifecycleHooks
{
    #region Engine lifecycle
    /// <summary>Targets the two installed-engine operations that populate ChunkRenderer manager tables.</summary>
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Constructor(typeof(ChunkRenderer), [typeof(int[]), typeof(ClientMain)]);
        yield return AccessTools.Method(typeof(ChunkRenderer), "RuntimeAddBlockTextureAtlas");
    }

    /// <summary>Publishes completed manager membership, including changes made within the same outer array.</summary>
    [HarmonyPostfix]
    internal static void Postfix(MeshDataPoolManager[][] ___poolsByRenderPass)
    {
        MeshPoolClassifier.Rebuild(___poolsByRenderPass);
        VanillaGraphicsExpanded.Rendering.Diagnostics.EngineMeshPoolDebugLabels.Register(___poolsByRenderPass);
    }
    #endregion
}
