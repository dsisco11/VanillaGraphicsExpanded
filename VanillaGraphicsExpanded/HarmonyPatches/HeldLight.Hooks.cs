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
internal static class HeldLightHooks
{
    #region Patch ownership
    /// <summary>Installs only held-light hooks under the subsystem owner, outside the mod-wide PatchAll scan.</summary>
    internal static void Install(Harmony harmony)
    {
        // Register collection last: a transpiler compatibility failure must also retire earlier hooks.
        harmony.Patch(AccessTools.Method(typeof(EntityShapeRenderer), "RenderItem"),
            prefix: new HarmonyMethod(typeof(HeldLightHooks), nameof(Capture)));
        harmony.Patch(AccessTools.Method(typeof(PerceptionEffects), nameof(PerceptionEffects.ApplyToTpPlayer)),
            prefix: new HarmonyMethod(typeof(HeldLightHooks), nameof(ApplyPerception)));
        harmony.Patch(AccessTools.Method(typeof(PerceptionEffects), nameof(PerceptionEffects.OnBeforeGameRender)),
            prefix: new HarmonyMethod(typeof(HeldLightHooks), nameof(UpdatePerception)));
        harmony.Patch(AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender"),
            postfix: new HarmonyMethod(typeof(HeldLightHooks), nameof(Complete)));
        harmony.Patch(AccessTools.Method(typeof(SystemRenderPlayerEffects), "onBeforeRender"),
            prefix: new HarmonyMethod(typeof(HeldLightHooks), nameof(Begin)),
            transpiler: new HarmonyMethod(typeof(HeldLightHooks), nameof(Collect)),
            finalizer: new HarmonyMethod(typeof(HeldLightHooks), nameof(Finish)));
    }
    #endregion

    #region Collection and animation boundaries
    /// <summary>Clears pending attachment work at the engine's light-array reset boundary.</summary>
    private static void Begin(SystemRenderPlayerEffects __instance) => HeldLightSystem.Begin(__instance);

    /// <summary>Handles only exceptions attributed to the held-light extension of collection.</summary>
    private static Exception? Finish(Exception? __exception) => HeldLightSystem.FinishCollection(__exception);

    /// <summary>Passes entity identity through the one entity-light call without replacing engine enumeration.</summary>
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
            code[i].operand = AccessTools.Method(typeof(HeldLightSystem), nameof(HeldLightSystem.AddEntityLight));
            matches++;
        }
        if (matches != 1) throw new InvalidOperationException($"Expected one engine entity-light call; found {matches}.");
        return code;
    }

    /// <summary>Publishes current hand positions before terrain or deferred lighting consumes the shared arrays.</summary>
    private static void Complete(SystemRenderEntities __instance, float __0) => HeldLightSystem.Complete(__instance, __0);
    #endregion

    #region Draw-free item evaluation
    /// <summary>Leaves pose-mutating perception callbacks to the live renderer's normal invocation.</summary>
    private static bool ApplyPerception(EntityPlayer entityPlr) => HeldLightSystem.ApplyPerception(entityPlr);

    /// <summary>A recovery collection must not advance perception a second time.</summary>
    private static bool UpdatePerception() => HeldLightSystem.ShouldUpdatePerception;

    /// <summary>Captures item placement only for the temporary attachment renderer.</summary>
    private static bool Capture(EntityShapeRenderer __instance, ItemStack stack, AttachmentPointAndPose apap, ItemRenderInfo renderInfo)
        => HeldLightSystem.Capture(__instance, stack, apap, renderInfo);
    #endregion
}
