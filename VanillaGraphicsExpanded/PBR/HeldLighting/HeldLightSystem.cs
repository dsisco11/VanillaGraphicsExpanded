using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;
using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.PBR.HeldLighting;

/// <summary>Owns held-light patches and contains failures without disabling other VGE rendering systems.</summary>
internal static class HeldLightSystem
{
    internal const string PatchId = Constants.ModId + ".heldlighting";
    private static Harmony? patches;
    private static Action<string>? log;
    private static SystemRenderPlayerEffects? collector;
    private static ClientMain? game;
    private static double[]? collectionView;
    private static Exception? collectionFailure;
    private static Action<SystemRenderPlayerEffects, byte[], EntityPos>? vanillaLight;
    private static bool resolving;
    private static bool perceptionUpdated;
    internal static bool Enabled { get; private set; }
    internal static bool Recovering { get; private set; }
    internal static bool ShouldUpdatePerception => !Recovering || !perceptionUpdated;

    #region Lifecycle
    /// <summary>Starts one mod-session installation; partial installation failures retire only this owner's hooks.</summary>
    internal static void Start(Action<string> logger)
    {
        Stop();
        log = logger;
        patches = new Harmony(PatchId);
        try
        {
            vanillaLight = AccessTools.MethodDelegate<Action<SystemRenderPlayerEffects, byte[], EntityPos>>(
                AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)]));
            HeldLightHooks.Install(patches);
            Enabled = true;
        }
        catch (Exception error)
        {
            Disable(error, "patch installation", false);
        }
    }

    /// <summary>Releases subsystem patches and session references during mod disposal.</summary>
    internal static void Stop()
    {
        Enabled = false;
        RemovePatches();
        ClearFrame();
        log = null;
    }

    /// <summary>Logs a complete exception without allowing a failing logger to turn recovery into a crash.</summary>
    private static void Report(string operation, Exception error)
    {
        try { log?.Invoke($"[VGE HeldLighting] {operation}\n{error}"); }
        catch { /* The recovery path must remain usable if the logging sink itself fails. */ }
    }

    /// <summary>Delegates removal of this subsystem's patches to Harmony's owner-scoped cleanup.</summary>
    private static void RemovePatches()
    {
        if (patches == null) return;
        try { patches.UnpatchAll(PatchId); }
        catch (Exception error) { Report("Could not unpatch held lighting; callbacks remain disabled.", error); }
        finally { patches = null; }
    }

    /// <summary>Clears retained engine owners and any partial attachment work, including after initialization failure.</summary>
    private static void ClearFrame()
    {
        collector = null;
        game = null;
        collectionView = null;
        collectionFailure = null;
        perceptionUpdated = false;
        try { HeldLightSources.Clear(); }
        catch (Exception error) { Report("Could not clear held-light attachment work.", error); }
    }
    #endregion

    #region Guarded engine boundaries
    /// <summary>Records the collection context before entering fallible held-light preparation.</summary>
    internal static void Begin(SystemRenderPlayerEffects effects)
    {
        if (!Enabled) return;
        collector = effects;
        collectionFailure = null;
        game = null;
        collectionView = null;
        perceptionUpdated = false;
        try
        {
            game = (ClientMain)AccessTools.Field(typeof(ClientSystem), "game").GetValue(effects)!;
            collectionView = game.MvMatrix.Count > 0 ? (double[])game.MvMatrix.Top.Clone() : null;
            HeldLightSources.Begin(effects);
        }
        catch (Exception error) { collectionFailure = error; throw; }
    }

    /// <summary>Attributes extension failures while allowing disabled, already-entered collectors to run vanilla code.</summary>
    internal static void AddEntityLight(SystemRenderPlayerEffects effects, byte[] combined, Entity entity)
    {
        if (!Enabled || entity is not EntityPlayer)
        {
            // This path also supports the current compiled collector during recovery, before it is unpatched.
            if (vanillaLight != null) vanillaLight(effects, combined, entity.Pos);
            else AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)])
                    .Invoke(effects, [combined, entity.Pos]);
            return;
        }
        try { HeldLightSources.AddEntityLight(effects, combined, entity); }
        catch (Exception error) { collectionFailure = error; throw; }
    }

    /// <summary>Aborts partial collection and suppresses only the exact exception raised inside held-light work.</summary>
    internal static Exception? FinishCollection(Exception? error)
    {
        if (Enabled && error == null) perceptionUpdated = true;
        if (error == null || !ReferenceEquals(error, collectionFailure)) return error;
        collectionFailure = null;
        Disable(error, "light collection", true);
        return null;
    }

    /// <summary>Contains attachment-resolution failures after the temporary renderer's finally blocks have unwound.</summary>
    internal static void Complete(ClientSystem system, float dt)
    {
        if (!Enabled) return;
        try
        {
            resolving = true;
            HeldLightSources.Complete(system, dt);
        }
        catch (Exception error)
        {
            resolving = false;
            Disable(error, "attachment resolution", true);
        }
        finally { resolving = false; }
    }

    /// <summary>Guards capture hooks used by both temporary evaluation and ordinary engine drawing.</summary>
    internal static bool Capture(EntityShapeRenderer renderer, ItemStack stack, AttachmentPointAndPose pose, ItemRenderInfo info)
    {
        if (!Enabled) return true;
        try { return HeldLightAttachment.Capture(renderer, stack, pose, info); }
        catch (Exception error)
        {
            // Wait for the resolver's finally to restore its temporary shadow and capture state before unpatching.
            if (resolving) throw;
            Disable(error, "item capture", true);
            return true;
        }
    }

    /// <summary>Contains hook initialization errors without suppressing failures in the engine perception callback.</summary>
    internal static bool ApplyPerception(EntityPlayer player)
    {
        if (!Enabled) return true;
        try { return HeldLightAttachment.ApplyPerception(player); }
        catch (Exception error)
        {
            if (resolving) throw;
            Disable(error, "perception hook", true);
            return true;
        }
    }
    #endregion

    #region Current-frame recovery
    /// <summary>Latches the subsystem off, restores vanilla lighting, and removes only held-light patches.</summary>
    private static void Disable(Exception error, string operation, bool restoreLights)
    {
        Enabled = false;
        Report($"Disabled for this mod session after an error during {operation}. Restoring vanilla held lights.", error);
        try
        {
            if (restoreLights) RestoreVanillaCollection();
        }
        finally
        {
            RemovePatches();
            ClearFrame();
        }
    }

    /// <summary>Rebuilds the complete engine list so partial hand entries and displaced capacity are both rolled back.</summary>
    private static void RestoreVanillaCollection()
    {
        bool pushed = false;
        Recovering = true;
        try
        {
            if (collector == null || game == null) return;
            // Other Before callbacks may have changed the stack. Use the original collection coordinate frame.
            if (collectionView != null)
            {
                game.MvMatrix.Push(collectionView);
                pushed = true;
            }
            // The disabled transpiler wrapper delegates to engine AddPointLight. Its normal reset discards
            // partial entries; rerunning the complete list restores lights previously excluded by capacity.
            AccessTools.Method(typeof(SystemRenderPlayerEffects), "onBeforeRender").Invoke(collector, [0f]);
        }
        catch (Exception error)
        {
            Report("Vanilla light-list recovery also failed; discarding this frame's partial dynamic lights.", error);
            if (game?.shUniforms != null) game.shUniforms.PointLightsCount = 0;
        }
        finally
        {
            try { if (pushed) game!.MvMatrix.Pop(); }
            catch (Exception error) { Report("Could not restore the collection matrix stack.", error); }
            Recovering = false;
        }
    }
    #endregion
}
