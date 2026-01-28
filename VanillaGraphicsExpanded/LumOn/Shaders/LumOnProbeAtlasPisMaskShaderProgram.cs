using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn probe-resolution PIS mask pass.
/// Writes a per-probe 64-bit mask (packed into RG32F) selecting which atlas texels to trace.
/// </summary>
public sealed class LumOnProbeAtlasPisMaskShaderProgram : GpuProgram
{
    public LumOnProbeAtlasPisMaskShaderProgram()
    {
        RegisterUniformBlockBinding("LumOnFrameUBO", LumOnUniformBuffers.FrameBinding, required: true);
    }

    protected override string VertexStageShaderName => "lumon_probe_atlas_trace";

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeAtlasPisMaskShaderProgram
        {
            PassName = "lumon_probe_atlas_pis_mask",
            AssetDomain = "vanillagraphicsexpanded"
        };

        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_atlas_pis_mask", instance);
    }

    #endregion

    #region Texture Samplers

    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 0); }

    public GpuTexture? ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 1); }

    public GpuTexture? ScreenProbeAtlasHistory { set => BindTexture2D("octahedralHistory", value, 2); }

    public GpuTexture? ScreenProbeAtlasMetaHistory { set => BindTexture2D("probeAtlasMetaHistory", value, 3); }

    #endregion

    #region Temporal Distribution Defines

    public int TexelsPerFrame { set => SetDefine(VgeShaderDefines.LumOnAtlasTexelsPerFrame, value.ToString(CultureInfo.InvariantCulture)); }

    #endregion

    #region Product Importance Sampling Defines (Phase 10)

    public bool EnsureProbePisDefines(
        bool enabled,
        float exploreFraction,
        int exploreCount,
        float minConfidenceWeight,
        float weightEpsilon,
        bool forceUniformMask,
        bool forceBatchSlicing)
    {
        bool changed = false;
        changed |= SetDefine(VgeShaderDefines.LumOnProbePisEnabled, enabled ? "1" : "0");
        changed |= SetDefine(VgeShaderDefines.LumOnProbePisExploreFraction, exploreFraction.ToString("0.0####", CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnProbePisExploreCount, exploreCount.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnProbePisMinConfidenceWeight, minConfidenceWeight.ToString("0.0####", CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnProbePisWeightEpsilon, weightEpsilon.ToString("0.0########", CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnProbePisForceUniformMask, forceUniformMask ? "1" : "0");
        changed |= SetDefine(VgeShaderDefines.LumOnProbePisForceBatchSlicing, forceBatchSlicing ? "1" : "0");
        return !changed;
    }

    #endregion
}
