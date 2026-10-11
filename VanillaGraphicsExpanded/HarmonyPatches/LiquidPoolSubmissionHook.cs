using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Publishes VGE liquid inputs after the engine has assigned the current pool's transform and origin.</summary>
[HarmonyPatch(typeof(MeshDataPool), nameof(MeshDataPool.RenderMesh))]
internal static class LiquidPoolSubmissionHook
{
    #region Engine callbacks
    /// <summary>Submits immediately before the pool forwards its indexed draw to the render API.</summary>
    [HarmonyPrefix]
    internal static bool Prefix(MeshDataPool __instance)
    {
        // MeshDataPoolManager stages mini-dimension state and origin before RenderMesh,
        // then restores transforms afterwards. Only our liquid owners use this boundary.
        if (LiquidGraphicsSubmission.TryDraw(__instance)) return false;
        return true;
    }
    #endregion
}
