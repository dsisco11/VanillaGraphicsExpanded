using System;
using System.Globalization;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn debug visualization overlay.
/// Renders probe grid, depth, normals, and other debug views.
/// </summary>
public class LumOnDebugShaderProgram : GpuProgram
{
    public LumOnDebugShaderProgram()
    {
        RegisterUniformBlockBinding("LumOnFrameUBO", LumOnUniformBuffers.FrameBinding, required: true);
        RegisterUniformBlockBinding("LumOnWorldProbeUBO", LumOnUniformBuffers.WorldProbeBinding, required: false);
    }

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        // Register all per-program-kind entrypoints (plus the legacy dispatcher).
        LumOnDebugShaderProgramFamily.Register(api);
    }

    #endregion

    #region World Probes (Phase 18)

    public int WorldProbeEnabled { set => SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, value != 0 ? "1" : "0"); }

    public bool EnsureWorldProbeClipmapDefines(
        bool enabled,
        float baseSpacing,
        int levels,
        int resolution,
        int worldProbeOctahedralTileSize,
        int worldProbeAtlasTexelsPerUpdate,
        int worldProbeDiffuseStride)
    {
        if (!enabled)
        {
            baseSpacing = 0;
            levels = 0;
            resolution = 0;
            worldProbeOctahedralTileSize = 0;
            worldProbeAtlasTexelsPerUpdate = 0;
            worldProbeDiffuseStride = 0;
        }

        bool changed = false;
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, enabled ? "1" : "0");
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, levels.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, resolution.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, baseSpacing.ToString("0.0####", CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeOctahedralSize, worldProbeOctahedralTileSize.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeAtlasTexelsPerUpdate, worldProbeAtlasTexelsPerUpdate.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeDiffuseStride, Math.Max(1, worldProbeDiffuseStride).ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeBindRadianceAtlas, enabled ? "1" : "0");
        return !changed;
    }

    public GpuTexture? WorldProbeRadianceAtlas { set => BindTexture2D("worldProbeRadianceAtlas", value, 19); }
    public GpuTexture? WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 22); }
    public GpuTexture? WorldProbeDist0 { set => BindTexture2D("worldProbeDist0", value, 23); }
    public GpuTexture? WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 24); }
    public GpuTexture? WorldProbeDebugState0 { set => BindTexture2D("worldProbeDebugState0", value, 25); }

    public float WorldProbeBaseSpacing { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, value.ToString("0.0####", CultureInfo.InvariantCulture)); }

    public int WorldProbeLevels { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, value.ToString(CultureInfo.InvariantCulture)); }

    public int WorldProbeResolution { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, value.ToString(CultureInfo.InvariantCulture)); }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture.
    /// </summary>
    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 0, GpuSamplers.NearestClamp); }

    /// <summary>
    /// G-buffer normals texture.
    /// </summary>
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 1, GpuSamplers.NearestClamp); }

    /// <summary>
    /// PatchId G-buffer (RGBA32UI) used by LumonScene debug views.
    /// </summary>
    public int GBufferPatchId { set => BindExternalTexture2D("gBufferPatchId", value, 29, GpuSamplers.NearestClamp); }

    /// <summary>
    /// Probe anchor positions (posWS.xyz, valid).
    /// </summary>
    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 2); }

    /// <summary>
    /// Probe anchor normals.
    /// </summary>
    public GpuTexture? ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 3); }

    /// <summary>
    /// Radiance SH texture 0 (for SH debug view).
    /// </summary>
    public GpuTexture? RadianceTexture0 { set => BindTexture2D("radianceTexture0", value, 4); }

    /// <summary>
    /// Radiance SH texture 1 (for SH debug view - second texture for full unpacking).
    /// </summary>
    public GpuTexture? RadianceTexture1 { set => BindTexture2D("radianceTexture1", value, 5); }

    /// <summary>
    /// Half-resolution indirect diffuse.
    /// </summary>
    public GpuTexture? IndirectHalf { set => BindTexture2D("indirectHalf", value, 6); }

    /// <summary>
    /// History metadata texture (depth, normal, accumCount) for temporal debug.
    /// </summary>
    public GpuTexture? HistoryMeta { set => BindTexture2D("historyMeta", value, 7); }

    /// <summary>
    /// Screen-probe atlas meta (confidence/flags) for probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasMeta { set => BindTexture2D("probeAtlasMeta", value, 8); }

    /// <summary>
    /// Screen-probe atlas radiance (current/temporal output) for probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasCurrent { set => BindTexture2D("probeAtlasCurrent", value, 9); }

    /// <summary>
    /// Screen-probe atlas radiance (filtered output) for probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasFiltered { set => BindTexture2D("probeAtlasFiltered", value, 10); }

    /// <summary>
    /// The atlas texture currently selected as gather input (raw vs filtered).
    /// </summary>
    public GpuTexture? ProbeAtlasGatherInput { set => BindTexture2D("probeAtlasGatherInput", value, 11); }

    /// <summary>
    /// Raw/trace probe-atlas radiance (pre-temporal). Used by probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasTrace { set => BindTexture2D("probeAtlasTrace", value, 27); }

    /// <summary>
    /// Phase 10: probe-resolution trace mask (RG32F packed uint bits).
    /// </summary>
    public GpuTexture? ProbeTraceMask { set => BindTexture2D("probeTraceMask", value, 26); }

    /// <summary>
    /// Phase 10: probe-resolution importance energy (R32F, sum of weights).
    /// </summary>
    public GpuTexture? ProbePisEnergy { set => BindTexture2D("probePisEnergy", value, 28); }

    /// <summary>
    /// Full-resolution indirect diffuse (upsampled) used by composite debug views.
    /// </summary>
    public GpuTexture? IndirectDiffuseFull { set => BindTexture2D("indirectDiffuseFull", value, 12); }

    /// <summary>
    /// Albedo source for composite debug views (fallback: captured scene).
    /// </summary>
    public GpuTexture? GBufferAlbedo { set => BindTexture2D("gBufferAlbedo", value, 13); }

    /// <summary>
    /// Material properties (roughness/metallic/emissive/reflectivity) for composite debug views.
    /// </summary>
    public int GBufferMaterial { set => BindExternalTexture2D("gBufferMaterial", value, 14, GpuSamplers.NearestClamp); }

    /// <summary>
    /// Direct diffuse radiance (direct lighting debug views).
    /// </summary>
    public GpuTexture? DirectDiffuse { set => BindTexture2D("directDiffuse", value, 15); }

    /// <summary>
    /// Direct specular radiance (direct lighting debug views).
    /// </summary>
    public GpuTexture? DirectSpecular { set => BindTexture2D("directSpecular", value, 16); }

    /// <summary>
    /// Emissive radiance (direct lighting debug views).
    /// </summary>
    public GpuTexture? Emissive { set => BindTexture2D("emissive", value, 17); }

    /// <summary>
    /// Full-resolution velocity texture (RGBA32F): RG = velocityUv, A = packed flags.
    /// </summary>
    public GpuTexture? VelocityTex { set => BindTexture2D("velocityTex", value, 18); }

    #endregion

    #region LumonScene (Phase 22)

    public int LumonSceneEnabled { set => Uniform("vge_lumonSceneEnabled", value); }

    public int LumonSceneTileSizeTexels { set => Uniform("vge_lumonSceneTileSizeTexels", value); }

    public int LumonSceneTilesPerAxis { set => Uniform("vge_lumonSceneTilesPerAxis", value); }

    public int LumonSceneTilesPerAtlas { set => Uniform("vge_lumonSceneTilesPerAtlas", value); }

    public GpuTexture? LumonScenePageTableMip0 { set => BindTexture2D("vge_lumonScenePageTableMip0", value, 30); }

    public GpuTexture? LumonSceneIrradianceAtlas { set => BindTexture2D("vge_lumonSceneIrradianceAtlas", value, 31); }

    #endregion

    // Per-frame state (sizes, matrices, zNear/zFar, probe grid params) is provided via LumOnFrameUBO.

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

    #region Composite Debug Defines (SetDefine migration)

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
