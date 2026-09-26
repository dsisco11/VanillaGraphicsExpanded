using System;
using System.Globalization;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;
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
public partial class LumOnDebugShaderProgram : LumOnShaderProgram
{
    #region Shader options
    /// <summary>Gets or sets the declared DirectVisibility shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.DirectVisibility))]
    public partial bool DirectVisibility { get; set; }

    /// <summary>Gets or sets the declared WorldProbeDiffuseStride shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeDiffuseStride))]
    public partial int WorldProbeDiffuseStride { get; set; }

    /// <summary>Gets or sets the declared WorldProbeOctahedralSize shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeOctahedralSize))]
    public partial int WorldProbeOctahedralSize { get; set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => ContractLookup.Value[PassName];

    /// <summary>Builds the immutable identity index after generated contract initialization completes.</summary>
    private static class ContractLookup
    {
        internal static readonly System.Collections.Immutable.ImmutableDictionary<string, GpuShaderContract> Value =
            System.Collections.Immutable.ImmutableDictionary.ToImmutableDictionary(Contracts, contract => contract.Identity);
    }

    internal LumOnNearFieldVisibilityBindings NearFieldVisibility => ((LumOnDebugProgramLayout)ProgramLayout).NearFieldVisibility;

    protected override GpuProgramLayout CreateLayout() => new LumOnDebugProgramLayout();

    private LumOnDebugProgramLayout Layout => (LumOnDebugProgramLayout)ProgramLayout;

    private LumOnDebugParamsUbo Params => Layout.Params;

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        // Declare each fullscreen view independently without linking unused views.
        LumOnDebugShaderProgramFamily.Register(api);
    }

    #endregion

    #region World probes

    /// <summary>Gets or sets the declared WorldProbes shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbes))]
    public partial bool WorldProbeEnabled { get; set; }

    /// <summary>Updates topology only for debug programs declaring world-probe sampling.</summary>
    public bool EnsureWorldProbeClipmapDefines(
        bool enabled,
        float baseSpacing,
        int levels,
        int resolution,
        int worldProbeOctahedralTileSize,
        int worldProbeAtlasTexelsPerUpdate,
        int worldProbeDiffuseStride)
    {
        // The renderer shares this owner type across debug programs with different memberships.
        if (!ProgramContract.Groups.Contains(LumOnShaderGroups.World)) return true;
        if (!enabled)
        {
            baseSpacing = 0;
            levels = 0;
            resolution = 0;
            worldProbeOctahedralTileSize = 0;
            worldProbeAtlasTexelsPerUpdate = 0;
            worldProbeDiffuseStride = 0;
        }

        bool changed = SetShaderOptions(options =>
        {
            options.Set(LumOnShaderOptions.WorldProbes, enabled);
            options.Set(LumOnShaderOptions.WorldProbeLevels, levels);
            options.Set(LumOnShaderOptions.WorldProbeResolution, resolution);
            options.Set(LumOnShaderOptions.WorldProbeBaseSpacing, baseSpacing);
            options.Set(LumOnShaderOptions.WorldProbeOctahedralSize, worldProbeOctahedralTileSize);
            options.Set(LumOnShaderOptions.WorldProbeDiffuseStride, Math.Max(1, worldProbeDiffuseStride));
        });
        return !changed;
    }

    public GpuTexture? WorldProbeRadianceAtlas { set => Layout.BindTexture2D(ProgramId, "worldProbeRadianceAtlas", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeVis0 { set => Layout.BindTexture2D(ProgramId, "worldProbeVis0", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeDist0 { set => Layout.BindTexture2D(ProgramId, "worldProbeDist0", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeMeta0 { set => Layout.BindTexture2D(ProgramId, "worldProbeMeta0", value?.TextureId ?? 0, LayoutWarn); }
    public GpuTexture? WorldProbeDebugState0 { set => Layout.BindTexture2D(ProgramId, "worldProbeDebugState0", value?.TextureId ?? 0, LayoutWarn); }

    /// <summary>Gets or sets the declared WorldProbeBaseSpacing shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeBaseSpacing))]
    public partial float WorldProbeBaseSpacing { get; set; }

    /// <summary>Gets or sets the declared WorldProbeLevels shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeLevels))]
    public partial int WorldProbeLevels { get; set; }

    /// <summary>Gets or sets the declared WorldProbeResolution shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeResolution))]
    public partial int WorldProbeResolution { get; set; }

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

    /// <summary>Binds the array of page-table layers indexed by scene chunk slot.</summary>
    public GpuTexture? LumonScenePageTableMip0 { set => Layout.TryBindSamplerTextureActive(ProgramId, "vge_lumonScenePageTableMip0", OpenTK.Graphics.OpenGL.TextureTarget.Texture2DArray, value?.TextureId ?? 0, samplerId: 0, LayoutWarn); }

    /// <summary>Binds the irradiance atlas array using the same target as its physical storage.</summary>
    public GpuTexture? LumonSceneIrradianceAtlas { set => Layout.TryBindSamplerTextureActive(ProgramId, "vge_lumonSceneIrradianceAtlas", OpenTK.Graphics.OpenGL.TextureTarget.Texture2DArray, value?.TextureId ?? 0, samplerId: 0, LayoutWarn); }

    /// <summary>Binds the captured material atlas array without changing its texture target.</summary>
    public GpuTexture? LumonSceneMaterialAtlas { set => Layout.TryBindSamplerTextureActive(ProgramId, "vge_lumonSceneMaterialAtlas", OpenTK.Graphics.OpenGL.TextureTarget.Texture2DArray, value?.TextureId ?? 0, samplerId: 0, LayoutWarn); }

    public GpuTexture? LumonSceneSurfaceLut { set => Layout.BindTexture2D(ProgramId, "vge_lumonSceneSurfaceLut", value?.TextureId ?? 0, LayoutWarn); }

    #endregion

    /// <summary>Binds the shared packed light payload for geometry diagnostics.</summary>
    public GpuTexture? TraceSceneLegacy { set => Layout.BindTexture3D(ProgramId, "traceSceneLegacy", value?.TextureId ?? 0, LayoutWarn); }

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

    /// <summary>Gets or sets the declared PbrComposite shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.PbrComposite))]
    public partial bool EnablePbrComposite { get; set; }

    /// <summary>Gets or sets the declared AmbientOcclusion shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AmbientOcclusion))]
    public partial bool EnableAO { get; set; }

    /// <summary>Gets or sets the declared ShortRangeAo shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ShortRangeAo))]
    public partial bool EnableShortRangeAo { get; set; }

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
