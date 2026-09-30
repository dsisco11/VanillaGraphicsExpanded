using System.Reflection;
using HarmonyLib;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Captures engine-owned liquid submission resources at their construction boundary.</summary>
[HarmonyPatch]
internal static class LiquidMeshSourceHook
{
    #region Engine construction
    /// <summary>Targets the installed renderer constructor explicitly.</summary>
    internal static MethodBase TargetMethod() => AccessTools.Constructor(typeof(ChunkRenderer), [typeof(int[]), typeof(ClientMain)]);
    /// <summary>Publishes references after all constructor field assignments have completed.</summary>
    internal static void Postfix(ChunkRenderer __instance, ClientMain ___game, Vec2f ___blockTextureSize)
        => LiquidMeshSource.Register(___game.api, __instance, ___blockTextureSize);
    #endregion
}
