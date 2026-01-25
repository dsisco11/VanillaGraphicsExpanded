using System.Globalization;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn debug visualization overlay.
/// Renders probe grid, depth, normals, and other debug views.
/// </summary>
public class LumOnDebugShaderProgram : GpuProgram
{
    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnDebugShaderProgram
        {
            PassName = "lumon_debug",
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("lumon_debug", instance);
    }

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

    public int WorldProbeSH0 { set => BindTexture2D("worldProbeSH0", value, 19); }
    public int WorldProbeSH1 { set => BindTexture2D("worldProbeSH1", value, 20); }
    public int WorldProbeSH2 { set => BindTexture2D("worldProbeSH2", value, 21); }
    public int WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 22); }
    public int WorldProbeDist0 { set => BindTexture2D("worldProbeDist0", value, 23); }
    public int WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 24); }
    public int WorldProbeDebugState0 { set => BindTexture2D("worldProbeDebugState0", value, 25); }
    public int WorldProbeSky0 { set => BindTexture2D("worldProbeSky0", value, 26); }

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

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture.
    /// </summary>
    public int PrimaryDepth { set => BindTexture2D("primaryDepth", value, 0); }

    /// <summary>
    /// G-buffer normals texture.
    /// </summary>
    public int GBufferNormal { set => BindTexture2D("gBufferNormal", value, 1); }

    /// <summary>
    /// Probe anchor positions (posWS.xyz, valid).
    /// </summary>
    public int ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    /// <summary>
    /// Probe anchor normals.
    /// </summary>
    public int ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 3); }

    /// <summary>
    /// Radiance SH texture 0 (for SH debug view).
    /// </summary>
    public int RadianceTexture0 { set => BindTexture2D("radianceTexture0", value, 4); }

    /// <summary>
    /// Radiance SH texture 1 (for SH debug view - second texture for full unpacking).
    /// </summary>
    public int RadianceTexture1 { set => BindTexture2D("radianceTexture1", value, 5); }

    /// <summary>
    /// Half-resolution indirect diffuse.
    /// </summary>
    public int IndirectHalf { set => BindTexture2D("indirectHalf", value, 6); }

    /// <summary>
    /// History metadata texture (depth, normal, accumCount) for temporal debug.
    /// </summary>
    public int HistoryMeta { set => BindTexture2D("historyMeta", value, 7); }

    /// <summary>
    /// Screen-probe atlas meta (confidence/flags) for probe-atlas debug modes.
    /// </summary>
    public int ProbeAtlasMeta { set => BindTexture2D("probeAtlasMeta", value, 8); }

    /// <summary>
    /// Screen-probe atlas radiance (current/temporal output) for probe-atlas debug modes.
    /// </summary>
    public int ProbeAtlasCurrent { set => BindTexture2D("probeAtlasCurrent", value, 9); }

    /// <summary>
    /// Screen-probe atlas radiance (filtered output) for probe-atlas debug modes.
    /// </summary>
    public int ProbeAtlasFiltered { set => BindTexture2D("probeAtlasFiltered", value, 10); }

    /// <summary>
    /// The atlas texture currently selected as gather input (raw vs filtered).
    /// </summary>
    public int ProbeAtlasGatherInput { set => BindTexture2D("probeAtlasGatherInput", value, 11); }

    /// <summary>
    /// Raw/trace probe-atlas radiance (pre-temporal). Used by probe-atlas debug modes.
    /// </summary>
    public int ProbeAtlasTrace { set => BindTexture2D("probeAtlasTrace", value, 27); }

    /// <summary>
    /// Full-resolution indirect diffuse (upsampled) used by composite debug views.
    /// </summary>
    public int IndirectDiffuseFull { set => BindTexture2D("indirectDiffuseFull", value, 12); }

    /// <summary>
    /// Albedo source for composite debug views (fallback: captured scene).
    /// </summary>
    public int GBufferAlbedo { set => BindTexture2D("gBufferAlbedo", value, 13); }

    /// <summary>
    /// Material properties (roughness/metallic/emissive/reflectivity) for composite debug views.
    /// </summary>
    public int GBufferMaterial { set => BindTexture2D("gBufferMaterial", value, 14); }

    /// <summary>
    /// Direct diffuse radiance (Phase 16 debug views).
    /// </summary>
    public int DirectDiffuse { set => BindTexture2D("directDiffuse", value, 15); }

    /// <summary>
    /// Direct specular radiance (Phase 16 debug views).
    /// </summary>
    public int DirectSpecular { set => BindTexture2D("directSpecular", value, 16); }

    /// <summary>
    /// Emissive radiance (Phase 16 debug views).
    /// </summary>
    public int Emissive { set => BindTexture2D("emissive", value, 17); }

    /// <summary>
    /// Full-resolution velocity texture (RGBA32F): RG = velocityUv, A = packed flags.
    /// </summary>
    public int VelocityTex { set => BindTexture2D("velocityTex", value, 18); }

    #endregion

    #region Size Uniforms

    /// <summary>
    /// Full-resolution screen size.
    /// </summary>
    public Vec2f ScreenSize { set => Uniform("screenSize", value); }

    /// <summary>
    /// Probe grid dimensions (probeCountX, probeCountY).
    /// </summary>
    public Vec2i ProbeGridSize { set => Uniform("probeGridSize", new Vec2f(value.X, value.Y)); }

    /// <summary>
    /// Spacing between probes in pixels.
    /// </summary>
    public int ProbeSpacing { set => Uniform("probeSpacing", value); }

    #endregion

    #region Z-Plane Uniforms

    /// <summary>
    /// Near clipping plane distance.
    /// </summary>
    public float ZNear { set => Uniform("zNear", value); }

    /// <summary>
    /// Far clipping plane distance.
    /// </summary>
    public float ZFar { set => Uniform("zFar", value); }

    #endregion

    #region Matrix Uniforms

    /// <summary>
    /// Inverse projection matrix for position reconstruction.
    /// </summary>
    public float[] InvProjectionMatrix { set => UniformMatrix("invProjectionMatrix", value); }

    /// <summary>
    /// Inverse view matrix for temporal reprojection.
    /// </summary>
    public float[] InvViewMatrix { set => UniformMatrix("invViewMatrix", value); }

    /// <summary>
    /// Previous frame view-projection matrix for temporal reprojection.
    /// </summary>
    public float[] PrevViewProjMatrix { set => UniformMatrix("prevViewProjMatrix", value); }

    #endregion

    #region Temporal Config Uniforms

    /// <summary>
    /// Temporal blend factor.
    /// </summary>
    public float TemporalAlpha { set => Uniform("temporalAlpha", value); }

    /// <summary>
    /// Depth rejection threshold.
    /// </summary>
    public float DepthRejectThreshold { set => Uniform("depthRejectThreshold", value); }

    /// <summary>
    /// Normal rejection threshold (dot product).
    /// </summary>
    public float NormalRejectThreshold { set => Uniform("normalRejectThreshold", value); }

    /// <summary>
    /// Velocity magnitude scale used by velocity debug views.
    /// Typically set to config.VelocityRejectThreshold.
    /// </summary>
    public float VelocityRejectThreshold { set => Uniform("velocityRejectThreshold", value); }

    #endregion

    #region Debug Uniforms

    /// <summary>
    /// Debug visualization mode.
    /// </summary>
    public int DebugMode { set => Uniform("debugMode", value); }

    /// <summary>
    /// Which atlas source is currently selected for gather input.
    /// 0=trace, 1=current (temporal), 2=filtered.
    /// </summary>
    public int GatherAtlasSource { set => Uniform("gatherAtlasSource", value); }

    #endregion

    #region Composite Debug Defines (Phase 15 → SetDefine migration)

    public float IndirectIntensity { set => Uniform("indirectIntensity", value); }

    public Vec3f IndirectTint { set => Uniform("indirectTint", value); }

    public bool EnablePbrComposite { set => SetDefine(VgeShaderDefines.LumOnPbrComposite, value ? "1" : "0"); }

    public bool EnableAO { set => SetDefine(VgeShaderDefines.LumOnEnableAo, value ? "1" : "0"); }

    public bool EnableShortRangeAo { set => SetDefine(VgeShaderDefines.LumOnEnableShortRangeAo, value ? "1" : "0"); }

    [System.Obsolete("Renamed to EnableShortRangeAo.")]
    public bool EnableBentNormal { set => EnableShortRangeAo = value; }

    public float DiffuseAOStrength { set => Uniform("diffuseAOStrength", value); }

    public float SpecularAOStrength { set => Uniform("specularAOStrength", value); }

    #endregion
}
