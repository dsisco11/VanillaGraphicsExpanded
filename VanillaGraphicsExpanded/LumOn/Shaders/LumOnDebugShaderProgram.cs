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
public partial class LumOnDebugShaderProgram : LumOnShaderProgram, ILumOnDebugShaderProgramBindings
{
    /// <summary>Supplies all direct-lighting radiance layers.</summary>
    public partial GpuTexture? DirectLighting { set; }


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

    /// <summary>Exposes retained parameters with an owner mutation guard.</summary>
    private LumOnDebugParamsUbo Params
    {
        get
        {
            var parameters = Layout.Params;
            parameters.SetWriteGuard(RequireInputMutation);
            return parameters;
        }
    }

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

    public partial GpuTexture? WorldProbeRadianceAtlas { set; }
    public partial GpuTexture? WorldProbeVis0 { set; }
    public partial GpuTexture? WorldProbeDist0 { set; }
    public partial GpuTexture? WorldProbeMeta0 { set; }
    public partial GpuTexture? WorldProbeDebugState0 { set; }

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
    public partial int PrimaryDepth { set; }

    /// <summary>
    /// G-buffer normals texture.
    /// </summary>
    public partial GpuTexture? GBufferSurface { set; }

    /// <summary>
    /// PatchId G-buffer (RGBA32UI) used by LumonScene debug views.
    /// </summary>
    public partial int GBufferPatchId { set; }

    /// <summary>
    /// Probe anchor positions (posWS.xyz, valid).
    /// </summary>
    public partial GpuTexture? ProbeAnchors { set; }

    /// <summary>
    /// Radiance SH texture 0 (for SH debug view).
    /// </summary>
    public partial GpuTexture? RadianceTexture0 { set; }

    /// <summary>
    /// Radiance SH texture 1 (for SH debug view - second texture for full unpacking).
    /// </summary>
    public partial GpuTexture? RadianceTexture1 { set; }

    /// <summary>
    /// Half-resolution indirect diffuse.
    /// </summary>
    public partial GpuTexture? IndirectHalf { set; }

    /// <summary>
    /// History metadata texture (depth, normal, accumCount) for temporal debug.
    /// </summary>
    public partial GpuTexture? HistoryMeta { set; }

    /// <summary>
    /// Screen-probe atlas meta (confidence/flags) for probe-atlas debug modes.
    /// </summary>
    public partial GpuTexture? ProbeAtlasMeta { set; }

    /// <summary>
    /// Screen-probe atlas radiance (current/temporal output) for probe-atlas debug modes.
    /// </summary>
    public partial GpuTexture? ProbeAtlasCurrent { set; }

    /// <summary>
    /// Screen-probe atlas radiance (filtered output) for probe-atlas debug modes.
    /// </summary>
    public partial GpuTexture? ProbeAtlasFiltered { set; }

    /// <summary>
    /// The atlas texture currently selected as gather input (raw vs filtered).
    /// </summary>
    public partial GpuTexture? ProbeAtlasGatherInput { set; }

    /// <summary>
    /// Raw/trace probe-atlas radiance (pre-temporal). Used by probe-atlas debug modes.
    /// </summary>
    public partial GpuTexture? ProbeAtlasTrace { set; }

    /// <summary>
    /// Phase 10: probe-resolution trace mask (RG32F packed uint bits).
    /// </summary>
    public partial GpuTexture? ProbeTraceMask { set; }

    /// <summary>
    /// Phase 10: probe-resolution importance energy (R32F, sum of weights).
    /// </summary>
    public partial GpuTexture? ProbePisEnergy { set; }

    /// <summary>Controls the diagnostic display sensitivity without modifying lighting.</summary>
    public float WorldProbeEffectGain
    {
        set
        {
            Params.WorldProbeEffectGain = value;
        }
    }

    /// <summary>Paired full-resolution output with accepted world radiance zeroed.</summary>
    public GpuTexture? WorldProbeSuppressedLighting
    {
        set
        {
            WorldProbeSuppressedLightingTexture = value;
            Params.WorldProbeComparisonReady = value is not null;
        }
    }

    /// <summary>Full-resolution indirect diffuse used by composite debug views.</summary>
    public partial GpuTexture? IndirectDiffuseFull { set; }

    /// <summary>
    /// Albedo source for composite debug views (fallback: captured scene).
    /// </summary>
    public partial GpuTexture? GBufferAlbedo { set; }

    /// <summary>
    /// Full-resolution velocity texture (RGBA32F): RG = velocityUv, A = packed flags.
    /// </summary>
    public partial GpuTexture? VelocityTex { set; }

    #endregion

    #region LumonScene (Phase 22)

    public int LumonSceneEnabled
    {
        set
        {
            Params.LumonSceneEnabled = value;
        }
    }

    public int LumonSceneTileSizeTexels
    {
        set
        {
            Params.LumonSceneTileSizeTexels = value;
        }
    }

    public int LumonSceneTilesPerAxis
    {
        set
        {
            Params.LumonSceneTilesPerAxis = value;
        }
    }

    public int LumonSceneTilesPerAtlas
    {
        set
        {
            Params.LumonSceneTilesPerAtlas = value;
        }
    }

    /// <summary>Binds the array of page-table layers indexed by scene chunk slot.</summary>
    public partial GpuTexture? LumonScenePageTableMip0 { set; }

    /// <summary>Binds the irradiance atlas array using the same target as its physical storage.</summary>
    public partial GpuTexture? LumonSceneIrradianceAtlas { set; }

    /// <summary>Binds the captured material atlas array without changing its texture target.</summary>
    public partial GpuTexture? LumonSceneMaterialAtlas { set; }

    public partial GpuTexture? LumonSceneSurfaceLut { set; }

    #endregion

    /// <summary>Binds the shared packed light payload for geometry diagnostics.</summary>
    public partial GpuTexture? TraceSceneLegacy { set; }

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
        }
    }

    #endregion

    #region Composite Debug Defines (SetDefine migration)

    public float IndirectIntensity
    {
        set
        {
            Params.IndirectIntensity = value;
        }
    }

    public Vec3f IndirectTint
    {
        set
        {
            Params.IndirectTint = new System.Numerics.Vector3(value.X, value.Y, value.Z);
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
        }
    }

    public float SpecularAOStrength
    {
        set
        {
            Params.SpecularAOStrength = value;
        }
    }

    #endregion
    #region Binding sources
    /// <summary>Supplies shared lighting storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnDebugShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies shared lighting storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnDebugShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
    /// <summary>Supplies packed parameters for one publication per use.</summary>
    CpuUniformBuffer ILumOnDebugShaderProgramBindings.Parameters => Params;
    /// <summary>Supplies retained visibility parameters.</summary>
    CpuUniformBuffer ILumOnDebugShaderProgramBindings.LumOnNearField => NearFieldVisibility.Parameters;
    /// <summary>Supplies retained visibility geometry.</summary>
    GpuTexture? ILumOnDebugShaderProgramBindings.NearFieldGeometry => NearFieldVisibility.Geometry;
    /// <summary>Supplies retained visibility readiness.</summary>
    GpuTexture? ILumOnDebugShaderProgramBindings.NearFieldRegions => NearFieldVisibility.Regions;
    #endregion
}
