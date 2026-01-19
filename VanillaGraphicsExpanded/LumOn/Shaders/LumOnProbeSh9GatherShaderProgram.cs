using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for cheap gather from per-probe SH9 coefficients.
/// Intended for ProbeAtlasGatherMode.EvaluateProjectedSH.
/// </summary>
public class LumOnProbeSh9GatherShaderProgram : VgeShaderProgram
{
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

    public int ProbeSh0 { set => BindTexture2D("probeSh0", value, 0); }
    public int ProbeSh1 { set => BindTexture2D("probeSh1", value, 1); }
    public int ProbeSh2 { set => BindTexture2D("probeSh2", value, 2); }
    public int ProbeSh3 { set => BindTexture2D("probeSh3", value, 3); }
    public int ProbeSh4 { set => BindTexture2D("probeSh4", value, 4); }
    public int ProbeSh5 { set => BindTexture2D("probeSh5", value, 5); }
    public int ProbeSh6 { set => BindTexture2D("probeSh6", value, 6); }

    #endregion

    #region Probe Anchors

    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 7); }
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 8); }

    #endregion

    #region GBuffer Inputs

    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 9); }
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 10); }

    #endregion

    #region Matrix Uniforms

    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    public float[] ViewMatrix { set => UniformMatrix("viewMatrix", value); }

    public float[] InvViewMatrix { set => UniformMatrix("invViewMatrix", value); }

    #endregion

    #region Uniforms

    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    public Vec2f HalfResSize { set => Uniform("halfResSize", value); }

    public float ZNear { set => Uniform("zNear", value); }

    public float ZFar { set => Uniform("zFar", value); }

    public float Intensity { set => Uniform("intensity", value); }

    public float[] IndirectTint { set => Uniform("indirectTint", new Vec3f(value[0], value[1], value[2])); }

    #endregion

    #region World Probes (Phase 18)

    public int WorldProbeEnabled { set => SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, value != 0 ? "1" : "0"); }

    public int WorldProbeSH0 { set => BindTexture2D("worldProbeSH0", value, 11); }
    public int WorldProbeSH1 { set => BindTexture2D("worldProbeSH1", value, 12); }
    public int WorldProbeSH2 { set => BindTexture2D("worldProbeSH2", value, 13); }
    public int WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 14); }
    public int WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 15); }

    public Vec3f WorldProbeCameraPosWS { set => Uniform("worldProbeCameraPosWS", value); }

    public float WorldProbeBaseSpacing { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, value.ToString("0.0####", CultureInfo.InvariantCulture)); }

    public int WorldProbeLevels { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, value.ToString(CultureInfo.InvariantCulture)); }

    public int WorldProbeResolution { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, value.ToString(CultureInfo.InvariantCulture)); }

    public void SetWorldProbeLevelParams(int level, Vec3f originMinCorner, Vec3f ringOffset)
    {
        Uniform($"worldProbeOriginMinCorner[{level}]", originMinCorner);
        Uniform($"worldProbeRingOffset[{level}]", ringOffset);
    }

    #endregion
}
