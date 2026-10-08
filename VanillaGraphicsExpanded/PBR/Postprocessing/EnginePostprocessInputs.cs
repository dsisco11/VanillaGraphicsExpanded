using System;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Postprocessing;

/// <summary>Snapshots native graphics settings at the postprocess handoff.</summary>
internal readonly record struct EnginePostprocessInputs(bool Bloom,bool LightShafts,bool Ssao,bool Fxaa,int SsaoQuality,int LightShaftQuality=1)
{

    #region Public API
    /// <summary>Reads effect controls through the client settings API while preserving the engine's postprocessing gate.</summary>
    internal static EnginePostprocessInputs Capture(ClientPlatformAbstract platform,ICoreClientAPI api)
    {
        // Match the engine's graphics-setting decisions without depending on cached renderer flags.
        bool enabled=platform.DoPostProcessingEffects;
        int ssaoQuality=api.Settings.Int["ssaoQuality"];
        return new(api.Settings.Bool["bloom"]&&enabled,
            api.Settings.Int["godRays"]>0&&enabled,ssaoQuality>0&&enabled,
            api.Settings.Bool["fxaa"]&&enabled,ssaoQuality,Math.Clamp(api.Settings.Int["godRays"],0,3));
    }
    #endregion
}
