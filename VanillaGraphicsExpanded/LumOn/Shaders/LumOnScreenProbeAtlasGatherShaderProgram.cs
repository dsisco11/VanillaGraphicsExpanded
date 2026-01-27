using System.Globalization;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas gather pass.
/// Implementation detail: integrates radiance from an octahedral-mapped probe atlas.
/// This is the default screen-probe gather path.
/// </summary>
public class LumOnScreenProbeAtlasGatherShaderProgram : GpuProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasGatherShaderProgram
        {
            PassName = "lumon_probe_atlas_gather",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, "0");
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, "0");
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, "0");
        instance.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, "0.0");
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_probe_atlas_gather", instance);
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Screen-probe atlas radiance texture.
    /// Shader uniform name remains <c>octahedralAtlas</c> for compatibility.
    /// Layout: (probeCountX × 8, probeCountY × 8)
    /// Format: RGB = radiance, A = log-encoded hit distance
    /// </summary>
    public GpuTexture? ScreenProbeAtlas { set => BindTexture2D("octahedralAtlas", value, 0); }

    /// <summary>
    /// Probe anchor positions (world-space).
    /// Format: xyz = posWS, w = validity
    /// </summary>
    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 1); }

    /// <summary>
    /// Probe anchor normals (world-space, encoded).
    /// </summary>
    public GpuTexture? ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 2); }

    /// <summary>
    /// Primary depth texture (G-buffer).
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 3, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-buffer normals (world-space, encoded).
    /// </summary>
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 4, GpuSamplers.NearestClamp); }

    #endregion

    // Per-frame state (matrices, sizes, probe grid params, zNear/zFar) is provided via LumOnFrameUBO.

    #region Quality Uniforms

    /// <summary>
    /// Intensity multiplier for indirect lighting output.
    /// </summary>
    public float Intensity { set => Uniform("intensity", value); }

    /// <summary>
    /// RGB tint applied to indirect lighting.
    /// </summary>
    public float[] IndirectTint { set => Uniform("indirectTint", new Vec3f(value[0], value[1], value[2])); }

    /// <summary>
    /// Leak prevention threshold.
    /// If probe hit distance exceeds pixel depth × (1 + threshold),
    /// the contribution is reduced to prevent light leaking.
    /// Default: 0.5 (50% tolerance)
    /// </summary>
    public float LeakThreshold { set => Uniform("leakThreshold", value); }

    /// <summary>
    /// Sample stride for hemisphere integration.
    /// 1 = full quality (64 samples per probe)
    /// 2 = performance mode (16 samples per probe)
    /// </summary>
    public int SampleStride { set => Uniform("sampleStride", value); }

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

    public GpuTexture? WorldProbeSH0 { set => BindTexture2D("worldProbeSH0", value, 5); }
    public GpuTexture? WorldProbeSH1 { set => BindTexture2D("worldProbeSH1", value, 6); }
    public GpuTexture? WorldProbeSH2 { set => BindTexture2D("worldProbeSH2", value, 7); }
    public GpuTexture? WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 8); }
    public GpuTexture? WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 9); }
    public GpuTexture? WorldProbeSky0 { set => BindTexture2D("worldProbeSky0", value, 10); }

    public Vec3f WorldProbeCameraPosWS { set => Uniform("worldProbeCameraPosWS", value); }

    public Vec3f WorldProbeSkyTint { set => Uniform("worldProbeSkyTint", value); }

    public float WorldProbeBaseSpacing { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, value.ToString("0.0####", CultureInfo.InvariantCulture)); }

    public int WorldProbeLevels { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, value.ToString(CultureInfo.InvariantCulture)); }

    public int WorldProbeResolution { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, value.ToString(CultureInfo.InvariantCulture)); }

    public void SetWorldProbeLevelParams(int level, Vec3f originMinCorner, Vec3f ringOffset)
    {
        TrySetWorldProbeLevelParams(level, originMinCorner, ringOffset);
    }

    public bool TrySetWorldProbeLevelParams(int level, Vec3f originMinCorner, Vec3f ringOffset)
    {
        bool ok0 = TryUniformArrayElement("worldProbeOriginMinCorner", level, originMinCorner);
        bool ok1 = TryUniformArrayElement("worldProbeRingOffset", level, ringOffset);
        return ok0 && ok1;
    }

    #endregion
}
