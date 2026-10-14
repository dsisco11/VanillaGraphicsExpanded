using System;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;

/// <summary>Withdraws native night-sky drawing while preserving its client system and asset lifecycle.</summary>
internal static class NativeNightSkyDrawRegistration
{
    #region Public API
    /// <summary>Unregisters only the installed night-sky frame callback; repeated calls are harmless.</summary>
    internal static void Disable(ICoreClientAPI api)
    {
        if (api.World is not ClientMain game)
            throw new InvalidOperationException("Night-sky draw ownership requires the engine renderer registry.");
        // The native action registration is wrapped in DummyRenderer. Match its
        // exact draw owner/method, leaving systems, asset callbacks and other draws intact.
        for (int stage = 0; stage < game.eventManager.renderersByStage.Length; stage++)
            foreach (var handler in game.eventManager.renderersByStage[stage].ToArray())
                if (handler.Renderer is DummyRenderer wrapper
                    && wrapper.action.Target?.GetType().FullName == "Vintagestory.Client.NoObf.SystemRenderNightSky"
                    && wrapper.action.Method.Name == "OnRenderFrame3D")
                    game.eventManager.UnregisterRenderer(wrapper,(EnumRenderStage)stage);
    }
    #endregion
}
