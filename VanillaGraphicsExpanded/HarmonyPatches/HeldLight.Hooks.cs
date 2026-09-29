using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaGraphicsExpanded.PBR.HeldLighting;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Connects engine light collection and current animation publication to held-light ownership.</summary>
[HarmonyPatch]
internal static class HeldLightHooks
{
    #region Collection and animation boundaries
    /// <summary>Clears pending attachment work at the engine's light-array reset boundary.</summary>
    [HarmonyPatch(typeof(SystemRenderPlayerEffects), "onBeforeRender")]
    [HarmonyPrefix]
    private static void Begin(SystemRenderPlayerEffects __instance) => HeldLightSources.Begin(__instance);

    /// <summary>Passes entity identity through the one entity-light call without replacing engine enumeration.</summary>
    [HarmonyPatch(typeof(SystemRenderPlayerEffects), "onBeforeRender")]
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Collect(IEnumerable<CodeInstruction> instructions)
    {
        var code = new List<CodeInstruction>(instructions);
        var add = AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)]);
        var getPosition = AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.Pos));
        int matches = 0;
        for (int i = 1; i < code.Count; i++)
        {
            if (!code[i].Calls(add)) continue;
            if (!code[i - 1].Calls(getPosition))
                throw new InvalidOperationException("Held-light entity collection no longer matches the installed engine.");
            // Leave the Entity on the evaluation stack instead of reducing it to its feet position.
            code[i - 1].opcode = OpCodes.Nop;
            code[i - 1].operand = null;
            code[i].opcode = OpCodes.Call;
            code[i].operand = AccessTools.Method(typeof(HeldLightSources), nameof(HeldLightSources.AddEntityLight));
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException($"Expected one engine entity-light call; found {matches}.");
        return code;
    }

    /// <summary>Publishes current hand positions before terrain or deferred lighting consumes the shared arrays.</summary>
    [HarmonyPatch(typeof(SystemRenderEntities), "OnBeforeRender")]
    [HarmonyPostfix]
    private static void Complete(SystemRenderEntities __instance, float __0) => HeldLightSources.Complete(__instance, __0);
    #endregion

    #region Draw-free item evaluation
    /// <summary>Leaves pose-mutating perception callbacks to the live renderer's normal invocation.</summary>
    [HarmonyPatch(typeof(PerceptionEffects), nameof(PerceptionEffects.ApplyToTpPlayer))]
    [HarmonyPrefix]
    private static bool ApplyPerception(EntityPlayer entityPlr) => HeldLightAttachment.ApplyPerception(entityPlr);

    /// <summary>Captures item placement only for the temporary attachment renderer.</summary>
    [HarmonyPatch(typeof(EntityShapeRenderer), "RenderItem")]
    [HarmonyPrefix]
    private static bool Capture(EntityShapeRenderer __instance, ItemStack stack, AttachmentPointAndPose apap, ItemRenderInfo renderInfo)
        => HeldLightAttachment.Capture(__instance, stack, apap, renderInfo);
    #endregion
}
