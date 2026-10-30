using System;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.OpenGL;

/// <summary>Checks the engine-created context once per client and persists a newer launch request when necessary.</summary>
internal sealed class OpenGLStartupAdmission
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, OpenGLStartupAdmission> Admissions = new();
    private readonly ICoreClientAPI api;
    private readonly string message = string.Empty;
    private readonly bool admitted;

    #region Public API
    /// <summary>Shares one admission decision across all client systems before their startup callbacks run.</summary>
    internal static bool Get(ICoreClientAPI api) => Admissions.GetValue(api, client => new OpenGLStartupAdmission(client)).admitted;
    #endregion

    #region Private
    /// <summary>Checks the live context, saves the next-launch requirement, and queues a visible explanation.</summary>
    private OpenGLStartupAdmission(ICoreClientAPI api)
    {
        this.api = api;
        string actual;
        try { actual = GL.GetString(StringName.Version) ?? "unavailable"; }
        catch (Exception error)
        {
            actual = "unavailable";
            api.Logger.Error("[VGE] Could not read the current OpenGL version: {0}", error.Message);
        }

        admitted = RenderingContextRequirement.IsSupported(actual);
        if (admitted) return;

        string configured = ClientSettings.GlContextVersion;
        string requested = RenderingContextRequirement.UpgradeSetting(configured);
        // Settings affect window creation on the next process launch; never attempt to replace the live context.
        bool saved = false;
        try
        {
            ClientSettings.GlContextVersion = requested;
            saved = ClientSettings.Inst.Save(true);
        }
        catch (Exception error)
        {
            api.Logger.Error("[VGE] Could not save the OpenGL launch setting: {0}", error.Message);
        }

        message = saved
            ? $"[VGE] Rendering disabled: current OpenGL is {actual}; desktop OpenGL 4.5 or newer is required. Saved glContextVersion={requested}. Fully exit and restart the game. If this persists, check your graphics driver and OpenGL support."
            : $"[VGE] Rendering disabled: current OpenGL is {actual}; desktop OpenGL 4.5 or newer is required. Could not save clientsettings.json. Set glContextVersion to {requested} and fully restart the game.";
        api.Logger.Warning(message);
        // Chat is unavailable during startup. Deliver once the vanilla world has finished loading.
        api.Event.LevelFinalize += OnReady;
        api.Event.LeaveWorld += Detach;
    }

    /// <summary>Displays the restart requirement when the client's chat UI is ready.</summary>
    private void OnReady()
    {
        Detach();
        api.ShowChatMessage(message);
    }

    /// <summary>Releases notification subscriptions on delivery or an aborted world load.</summary>
    private void Detach()
    {
        api.Event.LevelFinalize -= OnReady;
        api.Event.LeaveWorld -= Detach;
    }
    #endregion
}
