using System;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.Client.NoObf;
using VanillaGraphicsExpanded.HarmonyPatches;

namespace VanillaGraphicsExpanded.PBR.HeldLighting;

/// <summary>Owns held-light patches and contains failures without disabling other VGE rendering systems.</summary>
internal static class HeldLightSystem
{
    internal const string PatchId = Constants.ModId + ".heldlighting";
    private static Harmony? patches;
    private static Action<string>? log;
    private static ClientMain? game;
    private static Exception? collectionFailure;
    private static Action<SystemRenderPlayerEffects, byte[], EntityPos>? vanillaLight;
    private static ICoreClientAPI? api;
    private static HeldLightRenderer? renderer;
    internal static bool Enabled { get; private set; }

    #region Lifecycle
    /// <summary>Starts one mod-session installation; partial installation failures retire only this owner's hooks.</summary>
    internal static void Start(ICoreClientAPI clientApi, Action<string> logger)
    {
        Stop();
        log = logger;
        api = clientApi;
        patches = new Harmony(PatchId);
        try
        {
            vanillaLight = AccessTools.MethodDelegate<Action<SystemRenderPlayerEffects, byte[], EntityPos>>(
                AccessTools.Method(typeof(SystemRenderPlayerEffects), "AddPointLight", [typeof(byte[]), typeof(EntityPos)]));
            renderer = new HeldLightRenderer();
            api.Event.RegisterRenderer(renderer, EnumRenderStage.Before, "vge-held-lights");
            HeldLightHooks.Install(patches);
            Enabled = true;
        }
        catch (Exception error)
        {
            Disable(error, "patch installation");
        }
    }

    /// <summary>Releases subsystem patches and session references during mod disposal.</summary>
    internal static void Stop()
    {
        Enabled = false;
        RemoveRenderer();
        RemovePatches();
        ClearFrame();
        log = null;
        api = null;
    }

    /// <summary>Unregisters the renderer and disables callbacks retained by an in-progress render dispatch.</summary>
    private static void RemoveRenderer()
    {
        if (renderer == null) return;
        renderer.Dispose();
        try { api?.Event.UnregisterRenderer(renderer, EnumRenderStage.Before); }
        catch (Exception error) { Report("Could not unregister held-light renderer; its callback remains disabled.", error); }
        finally { renderer = null; }
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
        game = null;
        collectionFailure = null;
        try { HeldLightSources.Clear(); }
        catch (Exception error) { Report("Could not clear held-light attachment work.", error); }
    }
    #endregion

    #region Guarded engine boundaries
    /// <summary>Records the collection context before entering fallible held-light preparation.</summary>
    internal static void Begin(ClientMain client)
    {
        if (!Enabled) return;
        collectionFailure = null;
        game = null;
        try
        {
            game = client;
            HeldLightSources.Begin(game);
        }
        catch (Exception error) { collectionFailure = error; throw; }
    }

    /// <summary>Attributes extension failures while allowing disabled, already-entered collectors to run vanilla code.</summary>
    internal static void AddEntityLight(SystemRenderPlayerEffects effects, byte[] combined, Entity entity)
    {
        if (!Enabled || entity is not EntityPlayer)
        {
            // Any callback retained after shutdown stays pass-through.
            // Bound before installing the collection patch; no reflective invocation is needed during a frame.
            vanillaLight!(effects, combined, entity.Pos);
            return;
        }
        try { HeldLightSources.AddEntityLight(effects, combined, entity, game!); }
        catch (Exception error) { collectionFailure = error; throw; }
    }

    /// <summary>Aborts partial collection and suppresses only the exact exception raised inside held-light work.</summary>
    internal static Exception? FinishCollection(Exception? error)
    {
        if (error == null || !ReferenceEquals(error, collectionFailure)) return error;
        collectionFailure = null;
        Disable(error, "light collection");
        return null;
    }

    /// <summary>Contains attachment failures at the scheduled renderer boundary.</summary>
    internal static void Complete()
    {
        if (!Enabled || game == null) return;
        try
        {
            HeldLightSources.Complete(game);
        }
        catch (Exception error)
        {
            Disable(error, "attachment resolution");
        }
    }

    #endregion

    #region Failure shutdown
    /// <summary>Retires held lighting; the next normal engine collection restores vanilla output.</summary>
    private static void Disable(Exception error, string operation)
    {
        Enabled = false;
        Report($"Disabled for this mod session after an error during {operation}. Vanilla lighting resumes next frame.", error);
        // Leave the failure frame alone: replaying collection would repeat unrelated engine side effects.
        RemoveRenderer();
        RemovePatches();
        ClearFrame();
    }
    #endregion
}
