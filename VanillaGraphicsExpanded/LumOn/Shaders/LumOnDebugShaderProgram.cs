using System;
using System.Globalization;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for LumOn debug visualization overlay.
/// Renders probe grid, depth, normals, and other debug views.
/// </summary>
public class LumOnDebugShaderProgram : GpuProgram
{
    internal LumOnNearFieldVisibilityBindings NearFieldVisibility => ((LumOnDebugProgramLayout)ProgramLayout).NearFieldVisibility;

    protected override GpuProgramLayout CreateLayout() => new LumOnDebugProgramLayout();

    private LumOnDebugProgramLayout Layout => (LumOnDebugProgramLayout)ProgramLayout;

    private LumOnDebugParamsUbo Params => Layout.Params;

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

    public GpuTexture? WorldProbeRadianceAtlas { set => Layout.BindTexture2D(ProgramId, "worldProbeRadianceAtlas", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeVis0 { set => Layout.BindTexture2D(ProgramId, "worldProbeVis0", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeDist0 { set => Layout.BindTexture2D(ProgramId, "worldProbeDist0", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeMeta0 { set => Layout.BindTexture2D(ProgramId, "worldProbeMeta0", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeDebugState0 { set => Layout.BindTexture2D(ProgramId, "worldProbeDebugState0", value?.TextureId ?? 0, LayoutWarn); }

    public float WorldProbeBaseSpacing { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, value.ToString("0.0####", CultureInfo.InvariantCulture)); }

    public int WorldProbeLevels { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, value.ToString(CultureInfo.InvariantCulture)); }

    public int WorldProbeResolution { set => SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, value.ToString(CultureInfo.InvariantCulture)); }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Primary depth texture.
    /// </summary>
    public int PrimaryDepth { set => Layout.BindNearestClamp2D(ProgramId, "primaryDepth", value, LayoutWarn); }

    /// <summary>
    /// G-buffer normals texture.
    /// </summary>
    public int GBufferNormal { set => Layout.BindNearestClamp2D(ProgramId, "gBufferNormal", value, LayoutWarn); }

    /// <summary>
    /// PatchId G-buffer (RGBA32UI) used by LumonScene debug views.
    /// </summary>
    public int GBufferPatchId { set => Layout.BindNearestClamp2D(ProgramId, "gBufferPatchId", value, LayoutWarn); }

    /// <summary>
    /// Probe anchor positions (posWS.xyz, valid).
    /// </summary>
    public GpuTexture? ProbeAnchorPosition { set => Layout.BindTexture2D(ProgramId, "probeAnchorPosition", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Probe anchor normals.
    /// </summary>
    public GpuTexture? ProbeAnchorNormal { set => Layout.BindTexture2D(ProgramId, "probeAnchorNormal", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Radiance SH texture 0 (for SH debug view).
    /// </summary>
    public GpuTexture? RadianceTexture0 { set => Layout.BindTexture2D(ProgramId, "radianceTexture0", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Radiance SH texture 1 (for SH debug view - second texture for full unpacking).
    /// </summary>
    public GpuTexture? RadianceTexture1 { set => Layout.BindTexture2D(ProgramId, "radianceTexture1", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Half-resolution indirect diffuse.
    /// </summary>
    public GpuTexture? IndirectHalf { set => Layout.BindTexture2D(ProgramId, "indirectHalf", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// History metadata texture (depth, normal, accumCount) for temporal debug.
    /// </summary>
    public GpuTexture? HistoryMeta { set => Layout.BindTexture2D(ProgramId, "historyMeta", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Screen-probe atlas meta (confidence/flags) for probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasMeta { set => Layout.BindTexture2D(ProgramId, "probeAtlasMeta", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Screen-probe atlas radiance (current/temporal output) for probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasCurrent { set => Layout.BindTexture2D(ProgramId, "probeAtlasCurrent", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Screen-probe atlas radiance (filtered output) for probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasFiltered { set => Layout.BindTexture2D(ProgramId, "probeAtlasFiltered", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// The atlas texture currently selected as gather input (raw vs filtered).
    /// </summary>
    public GpuTexture? ProbeAtlasGatherInput { set => Layout.BindTexture2D(ProgramId, "probeAtlasGatherInput", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Raw/trace probe-atlas radiance (pre-temporal). Used by probe-atlas debug modes.
    /// </summary>
    public GpuTexture? ProbeAtlasTrace { set => Layout.BindTexture2D(ProgramId, "probeAtlasTrace", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Phase 10: probe-resolution trace mask (RG32F packed uint bits).
    /// </summary>
    public GpuTexture? ProbeTraceMask { set => Layout.BindTexture2D(ProgramId, "probeTraceMask", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Phase 10: probe-resolution importance energy (R32F, sum of weights).
    /// </summary>
    public GpuTexture? ProbePisEnergy { set => Layout.BindTexture2D(ProgramId, "probePisEnergy", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>Controls the diagnostic display sensitivity without modifying lighting.</summary>
    public float WorldProbeEffectGain
    {
        set
        {
            Params.WorldProbeEffectGain = value;
            Params.BindTo(this, LumOnDebugParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>Paired full-resolution output with accepted world radiance zeroed.</summary>
    public GpuTexture? WorldProbeSuppressedLighting
    {
        set
        {
            Layout.BindTexture2D(ProgramId, "worldProbeSuppressedLighting", value?.TextureId ?? 0, LayoutWarn);
            Params.WorldProbeComparisonReady = value is not null;
            Params.BindTo(this, LumOnDebugParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>Full-resolution indirect diffuse used by composite debug views.</summary>
    public GpuTexture? IndirectDiffuseFull { set => Layout.BindTexture2D(ProgramId, "indirectDiffuseFull", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Albedo source for composite debug views (fallback: captured scene).
    /// </summary>
    public GpuTexture? GBufferAlbedo { set => Layout.BindTexture2D(ProgramId, "gBufferAlbedo", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Material properties (roughness/metallic/emissive/reflectivity) for composite debug views.
    /// </summary>
    public int GBufferMaterial { set => Layout.BindNearestClamp2D(ProgramId, "gBufferMaterial", value, LayoutWarn); }

    /// <summary>
    /// Direct diffuse radiance (direct lighting debug views).
    /// </summary>
    public GpuTexture? DirectDiffuse { set => Layout.BindTexture2D(ProgramId, "directDiffuse", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Direct specular radiance (direct lighting debug views).
    /// </summary>
    public GpuTexture? DirectSpecular { set => Layout.BindTexture2D(ProgramId, "directSpecular", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Emissive radiance (direct lighting debug views).
    /// </summary>
    public GpuTexture? Emissive { set => Layout.BindTexture2D(ProgramId, "emissive", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>
    /// Full-resolution velocity texture (RGBA32F): RG = velocityUv, A = packed flags.
    /// </summary>
    public GpuTexture? VelocityTex { set => Layout.BindTexture2D(ProgramId, "velocityTex", value?.TextureId ?? 0, LayoutWarn); }

    #endregion

    #region LumonScene (Phase 22)

    public int LumonSceneEnabled
    {
        set
        {
            Params.LumonSceneEnabled = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public int LumonSceneTileSizeTexels
    {
        set
        {
            Params.LumonSceneTileSizeTexels = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public int LumonSceneTilesPerAxis
    {
        set
        {
            Params.LumonSceneTilesPerAxis = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public int LumonSceneTilesPerAtlas
    {
        set
        {
            Params.LumonSceneTilesPerAtlas = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public GpuTexture? LumonScenePageTableMip0 { set => Layout.BindTexture2D(ProgramId, "vge_lumonScenePageTableMip0", value?.TextureId ?? 0, LayoutWarn); }

    public GpuTexture? LumonSceneIrradianceAtlas { set => Layout.BindTexture2D(ProgramId, "vge_lumonSceneIrradianceAtlas", value?.TextureId ?? 0, LayoutWarn); }

    public GpuTexture? LumonSceneMaterialAtlas { set => Layout.BindTexture2D(ProgramId, "vge_lumonSceneMaterialAtlas", value?.TextureId ?? 0, LayoutWarn); }

    public GpuTexture? LumonSceneSurfaceLut { set => Layout.BindTexture2D(ProgramId, "vge_lumonSceneSurfaceLut", value?.TextureId ?? 0, LayoutWarn); }

    #endregion

    #region TraceScene (Phase 23)

    public int TraceSceneEnabled
    {
        set
        {
            Params.TraceSceneEnabled = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public int TraceSceneOccResolution
    {
        set
        {
            Params.TraceSceneOccResolution = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public VectorInt3 TraceSceneOccOriginMinCell0
    {
        set
        {
            Params.TraceSceneOccOriginMinCell0 = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public VectorInt3 TraceSceneOccRing0
    {
        set
        {
            Params.TraceSceneOccRing0 = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    // Keep this within typical GL_MAX_COMBINED_TEXTURE_IMAGE_UNITS on older drivers.
    public GpuTexture? TraceSceneOccL0 { set => Layout.BindTexture3D(ProgramId, "vge_traceOccL0", value?.TextureId ?? 0, LayoutWarn); }

    #endregion

    // Per-frame state (sizes, matrices, zNear/zFar, probe grid params) is provided via LumOnFrameUBO.

    #region Temporal Config Uniforms

    /// <summary>
    /// Temporal blend factor.
    /// </summary>
    public float TemporalAlpha
    {
        set
        {
            Params.TemporalAlpha = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Depth rejection threshold.
    /// </summary>
    public float DepthRejectThreshold
    {
        set
        {
            Params.DepthRejectThreshold = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Normal rejection threshold (dot product).
    /// </summary>
    public float NormalRejectThreshold
    {
        set
        {
            Params.NormalRejectThreshold = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion

    #region Debug Uniforms

    /// <summary>
    /// Debug visualization mode.
    /// </summary>
    public int DebugMode
    {
        set
        {
            Params.DebugMode = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Which atlas source is currently selected for gather input.
    /// 0=trace, 1=current (temporal), 2=filtered.
    /// </summary>
    public int GatherAtlasSource
    {
        set
        {
            Params.GatherAtlasSource = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion

    #region Composite Debug Defines (SetDefine migration)

    public float IndirectIntensity
    {
        set
        {
            Params.IndirectIntensity = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public Vec3f IndirectTint
    {
        set
        {
            Params.IndirectTint = new System.Numerics.Vector3(value.X, value.Y, value.Z);
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public bool EnablePbrComposite { set => SetDefine(VgeShaderDefines.LumOnPbrComposite, value ? "1" : "0"); }

    public bool EnableAO { set => SetDefine(VgeShaderDefines.LumOnEnableAo, value ? "1" : "0"); }

    public bool EnableShortRangeAo { set => SetDefine(VgeShaderDefines.LumOnEnableShortRangeAo, value ? "1" : "0"); }

    [System.Obsolete("Renamed to EnableShortRangeAo.")]
    public bool EnableBentNormal { set => EnableShortRangeAo = value; }

    public float DiffuseAOStrength
    {
        set
        {
            Params.DiffuseAOStrength = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    public float SpecularAOStrength
    {
        set
        {
            Params.SpecularAOStrength = value;
            Layout.BindParamsUbo(this, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion
}
