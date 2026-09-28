using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Profiling;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.HarmonyPatches;

/// <summary>Scopes actual engine dispatch, including every registered renderer and exceptional exits.</summary>
[HarmonyPatch(typeof(ClientEventManager), nameof(ClientEventManager.TriggerRenderStage), new[] { typeof(EnumRenderStage), typeof(float) })]
internal static class EngineRenderStageScopeHook
{
    #region Patch lifecycle
    /// <summary>Installs diagnostic dispatch only in builds that emit GPU debug groups.</summary>
    [HarmonyPrepare]
    private static bool Prepare()
    {
#if DEBUG
        return true;
#else
        return false;
#endif
    }

    /// <summary>Opens the stage at its real entry rather than an arbitrary renderer order.</summary>
    [HarmonyPrefix]
    internal static void Prefix(EnumRenderStage stage, out GlDebug.GroupScope __state) =>
        __state = GlDebug.Group(EngineRenderScopes.StageName(stage));

    /// <summary>Closes the stage on both normal and exceptional exits without suppressing exceptions.</summary>
    [HarmonyFinalizer]
    internal static void Finalizer(GlDebug.GroupScope __state) => __state.Dispose();
    #endregion

    #region Callback injection
    /// <summary>Preserves the handler at both engine callback sites so its registration name can label the draw work.</summary>
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.ToList();
        var render = AccessTools.Method(typeof(IRenderer), nameof(IRenderer.OnRenderFrame));
        var field = AccessTools.Field(typeof(RenderHandler), nameof(RenderHandler.Renderer));
        var wrapper = AccessTools.Method(typeof(EngineRenderScopes), nameof(EngineRenderScopes.Render));
        int count = 0;
        for (int i = 3; i < code.Count; i++)
        {
            if (!code[i].Calls(render)) continue;
            // Both engine branches load handler.Renderer, dt, stage immediately before invoking the callback.
            // Keep labels and exception markers on the existing instructions while replacing their operations.
            if (!code[i - 3].LoadsField(field) || code[i - 2].opcode != OpCodes.Ldarg_2 || code[i - 1].opcode != OpCodes.Ldarg_1)
                throw new InvalidOperationException("VGE render debug scopes: engine callback operand layout changed.");
            code[i - 3].opcode = OpCodes.Nop;
            code[i - 3].operand = null;
            code[i].opcode = OpCodes.Call;
            code[i].operand = wrapper;
            count++;
        }
        if (count != 2) throw new InvalidOperationException($"VGE render debug scopes: expected two engine callback sites, found {count}.");
        return code;
    }
    #endregion
}
