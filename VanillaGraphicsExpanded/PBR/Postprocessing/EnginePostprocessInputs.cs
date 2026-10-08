using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Postprocessing;

/// <summary>Snapshots native graphics settings at the postprocess handoff.</summary>
internal readonly record struct EnginePostprocessInputs(bool Bloom,bool GodRays,bool Ssao,bool Fxaa,int SsaoQuality)
{

    #region Public API
    /// <summary>Reads effect controls through the client settings API while preserving the engine's postprocessing gate.</summary>
    internal static EnginePostprocessInputs Capture(ClientPlatformWindows platform,ICoreClientAPI api)
    {
        // Match the engine's graphics-setting decisions without depending on cached renderer flags.
        bool enabled=platform.DoPostProcessingEffects;
        int ssaoQuality=api.Settings.Int["ssaoQuality"];
        return new(api.Settings.Bool["bloom"]&&enabled,
            api.Settings.Int["godRays"]>0&&enabled,ssaoQuality>0&&enabled,
            api.Settings.Bool["fxaa"]&&enabled,ssaoQuality);
    }
    #endregion
}
