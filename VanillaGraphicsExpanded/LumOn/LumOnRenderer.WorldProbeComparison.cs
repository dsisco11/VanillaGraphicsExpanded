using Newtonsoft.Json;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering.Profiling;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Runs a paired diagnostic through the production lighting passes with independent histories.</summary>
public partial class LumOnRenderer
{
    #region Comparison State

    private LumOnBufferManager? comparisonBuffers;
    private bool comparisonPass;
    private bool lightingPassesComplete;
    private int comparisonHistoryRevision = -1;
    private string? comparisonSettings;

    /// <summary>Whether the selected diagnostic needs a world-radiance-zeroed lighting branch.</summary>
    private bool WorldProbeComparisonRequested => config.LumOn.Enabled
        && config.LumOn.DebugMode is LumOnDebugMode.WorldProbeLightingEffect
            or LumOnDebugMode.WorldProbeSuppressedLighting;

    #endregion

    #region Comparison Lifecycle

    /// <summary>Allocates debug-only outputs and resets both histories when comparison inputs change.</summary>
    private void PrepareWorldProbeComparison()
    {
        if (!WorldProbeComparisonRequested)
            return;

        // A settings change can alter history interpretation or atlas dimensions. Recreate the
        // diagnostic resources together with a paired reset instead of comparing incompatible frames.
        string settings = JsonConvert.SerializeObject(config.LumOn)
            + JsonConvert.SerializeObject(config.WorldProbeClipmap);
        if (comparisonSettings != settings)
        {
            comparisonBuffers?.Dispose();
            comparisonBuffers = null;
            comparisonSettings = settings;
            isFirstFrame = true;
        }

        comparisonBuffers ??= new LumOnBufferManager(capi, config);
        if (!comparisonBuffers.EnsureBuffers(capi.Render.FrameWidth, capi.Render.FrameHeight)
            || comparisonHistoryRevision != primaryBuffers.HistoryRevision)
        {
            isFirstFrame = true;
        }
    }

    /// <summary>Releases diagnostic storage immediately when its views are no longer selected.</summary>
    private void ReleaseWorldProbeComparison()
    {
        comparisonBuffers?.Dispose();
        comparisonBuffers = null;
        comparisonSettings = null;
        comparisonHistoryRevision = -1;
        primaryBuffers.WorldProbeSuppressedLighting = null;
    }

    #endregion

    #region Paired Rendering

    /// <summary>Reuses the normal pass sequence with zeroed world radiance and separate temporal history.</summary>
    private void RenderWorldProbeComparison(FrameBufferRef primaryFb)
    {
        if (comparisonBuffers is null || !WorldProbeComparisonRequested)
            return;

        if (!lightingPassesComplete)
        {
            // A queued shader recompile can skip a pass. Never label a stale branch as a valid difference.
            isFirstFrame = true;
            return;
        }

        using var gpuScope = GlGpuProfiler.Instance.Scope("LumOn.WorldProbeComparison");
        comparisonPass = true;
        try
        {
            // Geometry, velocity, HZB, frame index and the normal branch's PIS mask are shared.
            // Only radiance-bearing outputs and histories differ; world validity is never disabled.
            RenderProbeAtlasTracePass(primaryFb);
            RenderProbeAtlasTemporalPass();
            RenderProbeAtlasFilterPass();
            if (config.LumOn.ProbeAtlasGather == VgeConfig.ProbeAtlasGatherMode.EvaluateProjectedSH)
                RenderProbeAtlasProjectSh9Pass();
            RenderGatherPass(primaryFb);
            RenderUpsamplePass(primaryFb);

            if (lightingPassesComplete)
            {
                primaryBuffers.WorldProbeSuppressedLighting = comparisonBuffers.IndirectFullTex;
                comparisonBuffers.SwapRadianceBuffers();
                comparisonHistoryRevision = primaryBuffers.HistoryRevision;
            }
            else
            {
                isFirstFrame = true;
            }
        }
        finally
        {
            // Production consumers must always retain the normal branch's outputs.
            comparisonPass = false;
        }
    }

    #endregion
}
