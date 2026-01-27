using System.Globalization;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for cheap gather from per-probe SH9 coefficients.
/// Intended for ProbeAtlasGatherMode.EvaluateProjectedSH.
/// </summary>
public class LumOnProbeSh9GatherShaderProgram : GpuProgram
{
    public LumOnProbeSh9GatherShaderProgram()
    {
        RegisterUniformBlockBinding("LumOnFrameUBO", LumOnUniformBuffers.FrameBinding, required: true);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnProbeSh9GatherShaderProgram
        {
            PassName = "lumon_probe_sh9_gather",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, "0");
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, "0");
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, "0");
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, "0.0");
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_sh9_gather", instance);
    }

    #endregion

    #region SH9 Textures

    public GpuTexture? ProbeSh0 { set => BindTexture2D("probeSh0", value, 0); }
    public GpuTexture? ProbeSh1 { set => BindTexture2D("probeSh1", value, 1); }
    public GpuTexture? ProbeSh2 { set => BindTexture2D("probeSh2", value, 2); }
    public GpuTexture? ProbeSh3 { set => BindTexture2D("probeSh3", value, 3); }
    public GpuTexture? ProbeSh4 { set => BindTexture2D("probeSh4", value, 4); }
    public GpuTexture? ProbeSh5 { set => BindTexture2D("probeSh5", value, 5); }
    public GpuTexture? ProbeSh6 { set => BindTexture2D("probeSh6", value, 6); }

    #endregion

    #region Probe Anchors

    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 7); }
    public GpuTexture? ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 8); }

    #endregion

    #region GBuffer Inputs

    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 9, GpuSamplers.NearestClamp); }
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 10, GpuSamplers.NearestClamp); }

    #endregion

    // Per-frame state (matrices, sizes, probe grid params, zNear/zFar) is provided via LumOnFrameUBO.

    #region Uniforms

    public float Intensity { set => Uniform("intensity", value); }

    public float[] IndirectTint { set => Uniform("indirectTint", new Vec3f(value[0], value[1], value[2])); }

    #endregion

    #region World Probes (Phase 18)

    public int WorldProbeEnabled { set => SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, value != 0 ? "1" : "0"); }

    public bool EnsureWorldProbeClipmapDefines(bool enabled, float baseSpacing, int levels, int resolution)
    {
        if (!enabled)
        {
            baseSpacing = 0;
            levels = 0;
            resolution = 0;
        }

        bool changed = false;
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, enabled ? "1" : "0");
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, levels.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, resolution.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, baseSpacing.ToString("0.0####", CultureInfo.InvariantCulture));
        return !changed;
    }

    public GpuTexture? WorldProbeSH0 { set => BindTexture2D("worldProbeSH0", value, 11); }
    public GpuTexture? WorldProbeSH1 { set => BindTexture2D("worldProbeSH1", value, 12); }
    public GpuTexture? WorldProbeSH2 { set => BindTexture2D("worldProbeSH2", value, 13); }
    public GpuTexture? WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 14); }
    public GpuTexture? WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 15); }
    public GpuTexture? WorldProbeSky0 { set => BindTexture2D("worldProbeSky0", value, 16); }

    public float WorldProbeBaseSpacing { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, value.ToString("0.0####", CultureInfo.InvariantCulture)); }

    public int WorldProbeLevels { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, value.ToString(CultureInfo.InvariantCulture)); }

    public int WorldProbeResolution { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, value.ToString(CultureInfo.InvariantCulture)); }

    #endregion
}
