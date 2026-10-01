using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Globalization;
using System.Collections.Generic;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.LumOn.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Shader program for the LumOn screen-probe atlas gather pass.
/// Implementation detail: integrates radiance from an octahedral-mapped probe atlas.
/// This is the default screen-probe gather path.
/// </summary>
[ShaderProgram("Contract", "lumon_probe_atlas_gather", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_atlas_gather.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_atlas_gather.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "World")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "WorldGather")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeBaseSpacing), SpecializationId = 11, When = "WorldProbeEnabled")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeDiffuseStride), SpecializationId = 15, When = "WorldProbeEnabled")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeLevels), SpecializationId = 12, When = "WorldProbeEnabled")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13, When = "WorldProbeEnabled")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14, When = "WorldProbeEnabled")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeEnabled))]
[ShaderBindingSet(typeof(ShaderInterfaceLocations), Defaults = true)]
[ShaderBindingSet(typeof(ShaderIncludeBindings), Defaults = true)]
public partial class LumOnScreenProbeAtlasGatherShaderProgram : LumOnShaderProgram
{

    #region Private: GPU binding declarations
    /// <summary>Declares the octahedralAtlas Sampler slot.</summary>
    [ShaderBinding("octahedralAtlas", ShaderBindingKind.Sampler, 0, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture OctahedralAtlas { set; }
    /// <summary>Declares the probeAnchorPosition Sampler slot.</summary>
    [ShaderBinding("probeAnchorPosition", ShaderBindingKind.Sampler, 1, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture ProbeAnchorPositionTexture { set; }
    /// <summary>Declares the probeAnchorNormal Sampler slot.</summary>
    [ShaderBinding("probeAnchorNormal", ShaderBindingKind.Sampler, 2, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture ProbeAnchorNormalTexture { set; }
    /// <summary>Declares the primaryDepth Sampler slot.</summary>
    [ShaderBinding("primaryDepth", ShaderBindingKind.Sampler, 3, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture PrimaryDepthTexture { set; }
    /// <summary>Declares the gBufferNormal Sampler slot.</summary>
    [ShaderBinding("gBufferNormal", ShaderBindingKind.Sampler, 4, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuTexture GBufferNormalTexture { set; }
    /// <summary>Declares the worldProbeRadianceAtlas Sampler slot.</summary>
    [ShaderBinding("worldProbeRadianceAtlas", ShaderBindingKind.Sampler, 5, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    private partial GpuTexture WorldProbeRadianceAtlasTexture { set; }
    /// <summary>Declares the worldProbeVis0 Sampler slot.</summary>
    [ShaderBinding("worldProbeVis0", ShaderBindingKind.Sampler, 8, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    private partial GpuTexture WorldProbeVis0Texture { set; }
    /// <summary>Declares the worldProbeMeta0 Sampler slot.</summary>
    [ShaderBinding("worldProbeMeta0", ShaderBindingKind.Sampler, 9, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    private partial GpuTexture WorldProbeMeta0Texture { set; }
    /// <summary>Declares the LumOnFrameUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnFrameUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Frame, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer LumOnFrame { set; }
    /// <summary>Declares the VgeLumOnProbeParamsUBO UniformBlock slot.</summary>
    [ShaderBinding("VgeLumOnProbeParamsUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Object, ShaderStageKind.Vertex, ShaderStageKind.Fragment)]
    private partial GpuUniformBuffer Parameters { set; }
    /// <summary>Declares the LumOnNearFieldUBO UniformBlock slot.</summary>
    [ShaderBinding("LumOnNearFieldUBO", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.Material, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    private partial GpuUniformBuffer LumOnNearField { set; }
    /// <summary>Declares the nearFieldGeometry Sampler slot.</summary>
    [ShaderBinding("nearFieldGeometry", ShaderBindingKind.Sampler, 6, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    private partial GpuTexture NearFieldGeometry { set; }
    /// <summary>Declares the nearFieldRegions Sampler slot.</summary>
    [ShaderBinding("nearFieldRegions", ShaderBindingKind.Sampler, 7, ShaderStageKind.Vertex, ShaderStageKind.Fragment, Required = false)]
    private partial GpuTexture NearFieldRegions { set; }
    #endregion

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
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    internal LumOnNearFieldVisibilityBindings NearFieldVisibility => ((LumOnScreenProbeAtlasGatherProgramLayout)ProgramLayout).NearFieldVisibility;

    protected override GpuProgramLayout CreateLayout() => new LumOnScreenProbeAtlasGatherProgramLayout();

    private LumOnProbeParamsUbo Params => paramsUbo ??= new LumOnProbeParamsUbo();

    /// <summary>
    /// Provides access to the underlying params UBO for advanced batched updates.
    /// Example:
    /// <code>
    /// using (program.ParamsUbo.BeginBatchUpdate())
    /// {
    ///     program.Intensity = 1.5f;
    ///     program.IndirectTint = new float[] { 1, 0.9f, 0.8f };
    ///     program.SampleStride = 2;
    ///     // Single GPU upload happens when scope exits
    /// }
    /// </code>
    /// </summary>
    public LumOnProbeParamsUbo ParamsUbo => Params;

    #region Diagnostic Controls

    /// <summary>
    /// Zeros accepted world radiance while preserving all metadata and sampling decisions.
    /// </summary>
    public bool SuppressWorldProbeRadiance
    {
        set
        {
            Params.SuppressWorldProbeRadiance = value;
            Params.BindTo(this, LumOnProbeParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion

    #region Static

    public static void Register(ICoreClientAPI api)
    {
        var instance = new LumOnScreenProbeAtlasGatherShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
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
    public float Intensity
    {
        set
        {
            Params.Intensity = value;
            Params.BindTo(this, LumOnProbeParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// RGB tint applied to indirect lighting.
    /// </summary>
    public float[] IndirectTint
    {
        set
        {
            Params.IndirectTint = new System.Numerics.Vector3(value[0], value[1], value[2]);
            Params.BindTo(this, LumOnProbeParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Leak prevention threshold.
    /// If probe hit distance exceeds pixel depth × (1 + threshold),
    /// the contribution is reduced to prevent light leaking.
    /// Default: 0.5 (50% tolerance)
    /// </summary>
    public float LeakThreshold
    {
        set
        {
            Params.LeakThreshold = value;
            Params.BindTo(this, LumOnProbeParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    /// <summary>
    /// Sample stride for hemisphere integration.
    /// 1 = full quality (64 samples per probe)
    /// 2 = performance mode (16 samples per probe)
    /// </summary>
    public int SampleStride
    {
        set
        {
            Params.SampleStride = value;
            Params.BindTo(this, LumOnProbeParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    #endregion

    #region World probes

    /// <summary>Gets or sets the declared WorldProbes shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbes))]
    public partial bool WorldProbeEnabled { get; set; }

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
            options.Set(LumOnShaderOptions.WorldProbeDiffuseStride, Math.Max(1, worldProbeDiffuseStride));
        });
        return !changed;
    }

    public GpuTexture? WorldProbeRadianceAtlas { set => BindTexture2D("worldProbeRadianceAtlas", value, 5); }
    public GpuTexture? WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 8); }
    public GpuTexture? WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 9); }

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
}
