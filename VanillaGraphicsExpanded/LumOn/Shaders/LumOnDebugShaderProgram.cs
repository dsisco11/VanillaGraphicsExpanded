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
[ShaderProgram("CompositeContract", "lumon_debug_composite", 16)]
[ShaderStage("CompositeContract", ShaderStageKind.Vertex, "lumon_debug_composite.vsh")]
[ShaderStage("CompositeContract", ShaderStageKind.Fragment, "lumon_debug_composite.fsh")]
[ShaderAcceptGroup("CompositeContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("CompositeContract", typeof(LumOnShaderGroups), "Composite")]
[ShaderAcceptGroup("CompositeContract", typeof(LumOnShaderGroups), "Ao")]
[ShaderUse("CompositeContract", ShaderStageKind.Fragment, nameof(EnableAO))]
[ShaderUse("CompositeContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("CompositeContract", ShaderStageKind.Fragment, nameof(EnablePbrComposite))]
[ShaderUse("CompositeContract", ShaderStageKind.Fragment, nameof(EnableShortRangeAo))]
[ShaderProgram("DirectContract", "lumon_debug_direct", 2)]
[ShaderStage("DirectContract", ShaderStageKind.Vertex, "lumon_debug_direct.vsh")]
[ShaderStage("DirectContract", ShaderStageKind.Fragment, "lumon_debug_direct.fsh")]
[ShaderAcceptGroup("DirectContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("DirectContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("GbufferContract", "lumon_debug_gbuffer", 2)]
[ShaderStage("GbufferContract", ShaderStageKind.Vertex, "lumon_debug_gbuffer.vsh")]
[ShaderStage("GbufferContract", ShaderStageKind.Fragment, "lumon_debug_gbuffer.fsh")]
[ShaderAcceptGroup("GbufferContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("GbufferContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("IndirectContract", "lumon_debug_indirect", 2)]
[ShaderStage("IndirectContract", ShaderStageKind.Vertex, "lumon_debug_indirect.vsh")]
[ShaderStage("IndirectContract", ShaderStageKind.Fragment, "lumon_debug_indirect.fsh")]
[ShaderAcceptGroup("IndirectContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("IndirectContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("ProbeAnchorsContract", "lumon_debug_probe_anchors", 2)]
[ShaderStage("ProbeAnchorsContract", ShaderStageKind.Vertex, "lumon_debug_probe_anchors.vsh")]
[ShaderStage("ProbeAnchorsContract", ShaderStageKind.Fragment, "lumon_debug_probe_anchors.fsh")]
[ShaderAcceptGroup("ProbeAnchorsContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("ProbeAnchorsContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("ProbeAtlasContract", "lumon_debug_probe_atlas", 2)]
[ShaderStage("ProbeAtlasContract", ShaderStageKind.Vertex, "lumon_debug_probe_atlas.vsh")]
[ShaderStage("ProbeAtlasContract", ShaderStageKind.Fragment, "lumon_debug_probe_atlas.fsh")]
[ShaderAcceptGroup("ProbeAtlasContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("ProbeAtlasContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("ShContract", "lumon_debug_sh", 2)]
[ShaderStage("ShContract", ShaderStageKind.Vertex, "lumon_debug_sh.vsh")]
[ShaderStage("ShContract", ShaderStageKind.Fragment, "lumon_debug_sh.fsh")]
[ShaderAcceptGroup("ShContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("ShContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("TemporalContract", "lumon_debug_temporal", 2)]
[ShaderStage("TemporalContract", ShaderStageKind.Vertex, "lumon_debug_temporal.vsh")]
[ShaderStage("TemporalContract", ShaderStageKind.Fragment, "lumon_debug_temporal.fsh")]
[ShaderAcceptGroup("TemporalContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("TemporalContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("VelocityContract", "lumon_debug_velocity", 2)]
[ShaderStage("VelocityContract", ShaderStageKind.Vertex, "lumon_debug_velocity.vsh")]
[ShaderStage("VelocityContract", ShaderStageKind.Fragment, "lumon_debug_velocity.fsh")]
[ShaderAcceptGroup("VelocityContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderUse("VelocityContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderProgram("WorldprobeContract", "lumon_debug_worldprobe", 4)]
[ShaderStage("WorldprobeContract", ShaderStageKind.Vertex, "lumon_debug_worldprobe.vsh")]
[ShaderStage("WorldprobeContract", ShaderStageKind.Fragment, "lumon_debug_worldprobe.fsh")]
[ShaderAcceptGroup("WorldprobeContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("WorldprobeContract", typeof(LumOnShaderGroups), "World")]
[ShaderAcceptGroup("WorldprobeContract", typeof(LumOnShaderGroups), "WorldGather")]
[ShaderUse("WorldprobeContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("WorldprobeContract", ShaderStageKind.Fragment, nameof(WorldProbeBaseSpacing), SpecializationId = 11, When = "WorldProbeEnabled")]
[ShaderUse("WorldprobeContract", ShaderStageKind.Fragment, nameof(WorldProbeDiffuseStride), SpecializationId = 15, When = "WorldProbeEnabled")]
[ShaderUse("WorldprobeContract", ShaderStageKind.Fragment, nameof(WorldProbeLevels), SpecializationId = 12, When = "WorldProbeEnabled")]
[ShaderUse("WorldprobeContract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13, When = "WorldProbeEnabled")]
[ShaderUse("WorldprobeContract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14, When = "WorldProbeEnabled")]
[ShaderUse("WorldprobeContract", ShaderStageKind.Fragment, nameof(WorldProbeEnabled))]
[ShaderProgram("DispatcherContract", "lumon_debug", 32)]
[ShaderStage("DispatcherContract", ShaderStageKind.Vertex, "lumon_debug.vsh")]
[ShaderStage("DispatcherContract", ShaderStageKind.Fragment, "lumon_debug.fsh")]
[ShaderAcceptGroup("DispatcherContract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("DispatcherContract", typeof(LumOnShaderGroups), "Composite")]
[ShaderAcceptGroup("DispatcherContract", typeof(LumOnShaderGroups), "Ao")]
[ShaderAcceptGroup("DispatcherContract", typeof(LumOnShaderGroups), "World")]
[ShaderAcceptGroup("DispatcherContract", typeof(LumOnShaderGroups), "WorldGather")]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(EnableAO))]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(EnablePbrComposite))]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(EnableShortRangeAo))]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(WorldProbeBaseSpacing), SpecializationId = 11, When = "WorldProbeEnabled")]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(WorldProbeDiffuseStride), SpecializationId = 15, When = "WorldProbeEnabled")]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(WorldProbeLevels), SpecializationId = 12, When = "WorldProbeEnabled")]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13, When = "WorldProbeEnabled")]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14, When = "WorldProbeEnabled")]
[ShaderUse("DispatcherContract", ShaderStageKind.Fragment, nameof(WorldProbeEnabled))]
public partial class LumOnDebugShaderProgram : GpuProgram
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
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => System.Linq.Enumerable.Single(Contracts, contract => contract.Identity == PassName);

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

        bool changed = false;
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbes, enabled);
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbeLevels, levels);
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbeResolution, resolution);
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbeBaseSpacing, baseSpacing);
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbeOctahedralSize, worldProbeOctahedralTileSize);
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbeDiffuseStride, Math.Max(1, worldProbeDiffuseStride));
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
