using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas trace pass.
/// Implementation detail: uses an octahedral-mapped direction atlas.
/// Ray traces from each probe and stores radiance + hit distance in the probe atlas.
/// Uses temporal distribution to trace a subset of directions each frame.
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_trace", 32)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_trace.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_trace.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Tracing")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Pis")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "World")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(TexelsPerFrame), SpecializationId = 1)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(BatchSlicing))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(EmissiveBoost), SpecializationId = 0)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(HzbCoarseMip), SpecializationId = 2)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(ImportanceSampling))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(NearField))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(RayMaxDistance), SpecializationId = 3)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(RaySteps), SpecializationId = 4)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(RayThickness), SpecializationId = 5)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(SkyMissWeight), SpecializationId = 6, When = "!NearField")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeBaseSpacing), SpecializationId = 11, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeLevels), SpecializationId = 12, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14, When = "WorldProbes")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbes))]
public partial class LumOnScreenProbeAtlasTraceShaderProgram : LumOnShaderProgram, ILumOnScreenProbeAtlasTraceShaderProgramBindings
{


    #region Shader options
    /// <summary>Gets or sets the declared BatchSlicing shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.BatchSlicing))]
    public partial bool BatchSlicing { get; set; }

    /// <summary>Gets or sets the declared DirectVisibility shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.DirectVisibility))]
    public partial bool DirectVisibility { get; set; }

    /// <summary>Gets or sets the declared EmissiveBoost shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.EmissiveBoost))]
    public partial float EmissiveBoost { get; set; }

    /// <summary>Gets or sets the declared ImportanceSampling shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.ImportanceSampling))]
    public partial bool ImportanceSampling { get; set; }

    /// <summary>Gets or sets the declared NearField shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.NearField))]
    public partial bool NearField { get; set; }

    /// <summary>Gets or sets the declared WorldProbeBaseSpacing shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeBaseSpacing))]
    public partial float WorldProbeBaseSpacing { get; set; }

    /// <summary>Gets or sets the declared WorldProbeLevels shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeLevels))]
    public partial int WorldProbeLevels { get; set; }

    /// <summary>Gets or sets the declared WorldProbeOctahedralSize shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeOctahedralSize))]
    public partial int WorldProbeOctahedralSize { get; set; }

    /// <summary>Gets or sets the declared WorldProbeResolution shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeResolution))]
    public partial int WorldProbeResolution { get; set; }

    /// <summary>Gets or sets the declared WorldProbes shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbes))]
    public partial bool WorldProbes { get; set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    protected override GpuProgramLayout CreateLayout() => new LumOnScreenProbeAtlasTraceProgramLayout();

    /// <summary>Exposes retained parameters with an owner mutation guard.</summary>
    private LumOnProbeParamsUbo Params
    {
        get
        {
            var parameters = paramsUbo ??= new LumOnProbeParamsUbo();
            parameters.SetWriteGuard(RequireInputMutation);
            return parameters;
        }
    }

    #region Diagnostic Controls

    /// <summary>
    /// Zeros accepted world radiance while preserving all metadata and sampling decisions.
    /// </summary>
    public bool SuppressWorldProbeRadiance
    {
        set
        {
            Params.SuppressWorldProbeRadiance = value;
        }
    }

    #endregion

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasTraceShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region Product Importance Sampling Defines

    /// <summary>Updates the importance-sampling switches consumed by this pass.</summary>
    public bool EnsureProbePisDefines(
        bool enabled,
        bool forceBatchSlicing)
    {
        bool changed = SetShaderOptions(options =>
        {
            options.Set(LumOnShaderOptions.ImportanceSampling, enabled);
            options.Set(LumOnShaderOptions.BatchSlicing, forceBatchSlicing);
        });
        return !changed;
    }

    #endregion

    #region Texture Samplers

    /// <summary>
    /// Probe anchor positions (posWS.xyz, valid) - stored in world-space.
    /// </summary>
    public partial GpuTexture? ProbeAnchorPosition { set; }

    /// <summary>
    /// Probe anchor normals (normalWS.xyz, reserved) - stored in world-space.
    /// </summary>
    public partial GpuTexture? ProbeAnchorNormal { set; }

    /// <summary>
    /// Primary depth texture for ray marching.
    /// </summary>
    public partial int PrimaryDepth { set; }

    /// <summary>
    /// LumOn-owned captured surface albedo.
    /// </summary>
    public partial GpuTexture? SurfaceAlbedo { set; }

    /// <summary>
    /// VGE material properties used to derive emissive radiance at ray hits.
    /// </summary>
    public partial int GBufferMaterial { set; }

    /// <summary>
    /// History probe atlas (octahedral-mapped) for temporal preservation.
    /// Shader uniform name remains <c>octahedralHistory</c> for compatibility.
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasHistory { set; }

    /// <summary>
    /// History probe-atlas meta (confidence + flags) for temporal preservation.
    /// </summary>
    public partial GpuTexture? ScreenProbeAtlasMetaHistory { set; }

    /// <summary>
    /// Probe-resolution trace mask (RG32F packed uint bits) selecting which atlas texels to trace.
    /// </summary>
    public partial GpuTexture? ProbeTraceMask { set; }

    /// <summary>
    /// Optional HZB depth pyramid (mipmapped R32F).
    /// </summary>
    public partial GpuTexture? HzbDepth { set; }

    #endregion

    // Per-frame state (matrices, screen size, probe grid size, frame index, zNear/zFar, sun/ambient colors)
    // is provided via LumOnFrameUBO.

    #region Temporal Distribution Defines

    /// <summary>
    /// Number of probe-atlas texels to trace per frame (default 8).
    /// With 64 texels total, this means full coverage in 8 frames.
    /// Compile-time define for temporal distribution.
    /// </summary>
    /// <summary>Gets or sets the declared AtlasTexelsPerFrame shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.AtlasTexelsPerFrame))]
    public partial int TexelsPerFrame { get; set; }

    #endregion

    #region Ray Tracing Defines

    /// <summary>
    /// Number of ray march steps.
    /// Compile-time define for loop bounds.
    /// </summary>
    /// <summary>Gets or sets the declared RaySteps shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RaySteps))]
    public partial int RaySteps { get; set; }

    /// <summary>
    /// Maximum ray march distance in view-space units.
    /// Compile-time define for trace distance.
    /// </summary>
    /// <summary>Gets or sets the declared RayMaxDistance shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RayMaxDistance))]
    public partial float RayMaxDistance { get; set; }

    /// <summary>
    /// Thickness threshold for depth test during ray marching.
    /// Compile-time define for hit threshold.
    /// </summary>
    /// <summary>Gets or sets the declared RayThickness shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.RayThickness))]
    public partial float RayThickness { get; set; }

    /// <summary>
    /// Coarse mip used for early rejection.
    /// Compile-time define for HZB mip selection.
    /// </summary>
    /// <summary>Gets or sets the declared HzbCoarseMip shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.HzbCoarseMip))]
    public partial int HzbCoarseMip { get; set; }

    #endregion

    #region Sky Fallback Uniforms

    /// <summary>
    /// Weight for sky color when ray misses (0 = black, 1 = full sky).
    /// Compile-time define for sky contribution.
    /// </summary>
    /// <summary>Gets or sets the declared SkyMissWeight shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.SkyMissWeight))]
    public partial float SkyMissWeight { get; set; }

    #endregion

    #region Indirect Lighting

    /// <summary>
    /// Tint color applied to indirect lighting.
    /// </summary>
    public Vec3f IndirectTint
    {
        set
        {
            Params.IndirectTint = new System.Numerics.Vector3(value.X, value.Y, value.Z);
        }
    }

    #endregion

    #region World probes

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

        bool changed = SetShaderOptions(options =>
        {
            options.Set(LumOnShaderOptions.WorldProbes, enabled);
            options.Set(LumOnShaderOptions.WorldProbeLevels, levels);
            options.Set(LumOnShaderOptions.WorldProbeResolution, resolution);
            options.Set(LumOnShaderOptions.WorldProbeBaseSpacing, baseSpacing);
            options.Set(LumOnShaderOptions.WorldProbeOctahedralSize, worldProbeOctahedralTileSize);
        });
        return !changed;
    }

    public partial GpuTexture? WorldProbeRadianceAtlas { set; }
    public partial GpuTexture? WorldProbeVis0 { set; }
    public partial GpuTexture? WorldProbeMeta0 { set; }

    #endregion
    #region Binding sources
    /// <summary>Supplies shared lighting storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasTraceShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies shared lighting storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnScreenProbeAtlasTraceShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
    /// <summary>Supplies packed parameters for one publication per use.</summary>
    CpuUniformBuffer ILumOnScreenProbeAtlasTraceShaderProgramBindings.Parameters => Params;
    #endregion
}
