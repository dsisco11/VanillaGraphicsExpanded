using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Extends the existing entity preparation owner without adding another animation tick or mesh registry.</summary>
internal static class NativeShadowEntityHooks
{
    private static readonly AccessTools.FieldRef<ClientMain, Dictionary<long, EntityRenderer>> ReadRenderers =
        AccessTools.FieldRefAccess<ClientMain, Dictionary<long, EntityRenderer>>("EntityRenderers");
    private static readonly AccessTools.FieldRef<ClientSystem, ClientMain> ReadGame =
        AccessTools.FieldRefAccess<ClientSystem, ClientMain>("game");
    private static ClientMain? game;
    private static NativeShadowEntityPreparation? owner;
    [ThreadStatic] private static NativeShadowEntityPreparation? current;
    [ThreadStatic] private static Entity? animating;

    #region Public API
    /// <summary>Installs only the qualified native preparation boundary; no rendering consumer is enabled.</summary>
    internal static void Install(Harmony harmony, ClientMain world, NativeShadowEntityPreparation preparation)
    {
        if (typeof(ClientMain).Assembly.ManifestModule.ModuleVersionId != new Guid("8a3ad3aa-2940-4c39-ae1b-881de9e65346") ||
            typeof(EntityShapeRenderer).Assembly.ManifestModule.ModuleVersionId != new Guid("0d8db7e9-fd74-4129-bbe8-1ff82baf23fc"))
            throw new NotSupportedException("Entity preparation requires the qualified native renderer modules.");
        if (owner is not null) throw new InvalidOperationException("An entity preparation owner is already attached.");
        harmony.Patch(AccessTools.Method(typeof(SystemRenderEntities), "OnBeforeRender"),
            prefix: new HarmonyMethod(typeof(NativeShadowEntityHooks), nameof(Begin)),
            transpiler: new HarmonyMethod(typeof(NativeShadowEntityHooks), nameof(Rewrite)),
            finalizer: new HarmonyMethod(typeof(NativeShadowEntityHooks), nameof(End)));
        game = world; owner = preparation;
    }

    /// <summary>Withdraws callbacks before the owning Harmony instance removes its patch.</summary>
    internal static void Detach() { owner = null; game = null; }

    /// <summary>Replaces only the three native preparation calls, preserving culling, iteration and exception handling.</summary>
    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var before = AccessTools.Method(typeof(EntityRenderer), nameof(EntityRenderer.BeforeRender));
        var get = AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.AnimManager));
        var advance = AccessTools.Method(typeof(IAnimationManager), nameof(IAnimationManager.OnClientFrame));
        int beforeCount = 0, getCount = 0, advanceCount = 0;
        foreach (var original in instructions)
        {
            var instruction = new CodeInstruction(original);
            if (instruction.Calls(before))
            {
                beforeCount++; instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(NativeShadowEntityHooks), nameof(Prepare));
            }
            else if (instruction.Calls(get))
            {
                getCount++;
                var dt = new CodeInstruction(OpCodes.Ldarg_1);
                dt.labels.AddRange(instruction.labels); instruction.labels.Clear();
                dt.blocks.AddRange(instruction.blocks); instruction.blocks.Clear();
                yield return dt;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(NativeShadowEntityHooks), nameof(Animation));
            }
            else if (instruction.Calls(advance))
            {
                advanceCount++; instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(NativeShadowEntityHooks), nameof(Advance));
            }
            yield return instruction;
        }
        if (beforeCount != 1 || getCount != 1 || advanceCount != 1)
            throw new NotSupportedException("Native entity preparation no longer has the qualified call structure.");
    }
    #endregion

    #region Private
    /// <summary>Captures the owner for exception-safe completion even if teardown occurs during native work.</summary>
    private static void Begin(SystemRenderEntities __instance, out NativeShadowEntityPreparation? __state)
    {
        __state = ReferenceEquals(ReadGame(__instance), game) ? owner : null;
        current = __state; animating = null;
        if (__state is not null) __state.Begin(ReadRenderers(game!));
    }

    /// <summary>Preserves normal preparation when no shadow owner is attached.</summary>
    private static void Prepare(EntityRenderer renderer, float dt)
    {
        if (current is { } active) active.PrepareVisible(renderer, dt);
        else renderer.BeforeRender(dt);
    }

    /// <summary>Supplies offscreen preparation before the original optional animation call.</summary>
    private static IAnimationManager? Animation(Entity entity, float dt)
    {
        animating = entity;
        return current is { } active ? active.AnimationFor(entity, dt) : entity.AnimManager;
    }

    /// <summary>Leaves the native tick count intact while requesting matrices for current shadow casters.</summary>
    private static void Advance(IAnimationManager animation, float dt)
    {
        if (current is { } active && animating is { } entity) active.Advance(entity, animation, dt);
        else animation.OnClientFrame(dt);
    }

    /// <summary>Never swallows native errors or publishes failed preparation as complete.</summary>
    private static Exception? End(Exception? __exception, NativeShadowEntityPreparation? __state)
    {
        try { __state?.End(__exception is not null); }
        finally { current = null; animating = null; }
        return __exception;
    }
    #endregion
}
