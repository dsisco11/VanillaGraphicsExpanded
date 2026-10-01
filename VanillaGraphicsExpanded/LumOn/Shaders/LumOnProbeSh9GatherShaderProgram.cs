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
/// Shader program for cheap gather from per-probe SH9 coefficients.
/// Intended for ProbeAtlasGatherMode.EvaluateProjectedSH.
/// </summary>
[ShaderProgram("Contract", "lumon_probe_sh9_gather", 4)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "lumon_probe_sh9_gather.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "lumon_probe_sh9_gather.fsh")]
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
public partial class LumOnProbeSh9GatherShaderProgram : LumOnShaderProgram, ILumOnProbeSh9GatherShaderProgramBindings
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
    internal override GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    internal LumOnNearFieldVisibilityBindings NearFieldVisibility => ((LumOnProbeSh9GatherProgramLayout)ProgramLayout).NearFieldVisibility;

    protected override GpuProgramLayout CreateLayout() => new LumOnProbeSh9GatherProgramLayout();

    /// <summary>Owns packed parameters and rejects writes during publication.</summary>
    private LumOnProbeParamsUbo Params
    {
        get
        {
            if (paramsUbo is null)
            {
                paramsUbo = new LumOnProbeParamsUbo();
                paramsUbo.SetWriteGuard(RequireInputMutation);
            }
            return paramsUbo;
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
        var instance = new LumOnProbeSh9GatherShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    #endregion

    #region SH9 Textures

    public partial GpuTexture? ProbeSh0 { set; }
    public partial GpuTexture? ProbeSh1 { set; }
    public partial GpuTexture? ProbeSh2 { set; }
    public partial GpuTexture? ProbeSh3 { set; }
    public partial GpuTexture? ProbeSh4 { set; }
    public partial GpuTexture? ProbeSh5 { set; }
    public partial GpuTexture? ProbeSh6 { set; }

    #endregion

    #region Probe Anchors

    public partial GpuTexture? ProbeAnchorPosition { set; }
    public partial GpuTexture? ProbeAnchorNormal { set; }

    #endregion

    #region GBuffer Inputs

    public partial int PrimaryDepth { set; }
    public partial int GBufferNormal { set; }

    #endregion

    // Per-frame state (matrices, sizes, probe grid params, zNear/zFar) is provided via LumOnFrameUBO.

    #region Uniforms

    public float Intensity
    {
        set
        {
            Params.Intensity = value;
        }
    }

    public float[] IndirectTint
    {
        set
        {
            Params.IndirectTint = new System.Numerics.Vector3(value[0], value[1], value[2]);
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

    public partial GpuTexture? WorldProbeRadianceAtlas { set; }
    public partial GpuTexture? WorldProbeVis0 { set; }
    public partial GpuTexture? WorldProbeMeta0 { set; }

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
    #region Binding sources
    /// <summary>Supplies current frame storage through the binding contract.</summary>
    GpuUniformBuffer? ILumOnProbeSh9GatherShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies retained world-probe storage when the installed variant consumes it.</summary>
    GpuUniformBuffer? ILumOnProbeSh9GatherShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
    /// <summary>Supplies the retained CPU block for one publication per use.</summary>
    CpuUniformBuffer ILumOnProbeSh9GatherShaderProgramBindings.Parameters => Params;
    /// <summary>Supplies retained visibility parameters through the declared block.</summary>
    CpuUniformBuffer ILumOnProbeSh9GatherShaderProgramBindings.LumOnNearField => NearFieldVisibility.Parameters;
    /// <summary>Supplies the retained geometry texture.</summary>
    GpuTexture? ILumOnProbeSh9GatherShaderProgramBindings.NearFieldGeometry => NearFieldVisibility.Geometry;
    /// <summary>Supplies the retained readiness texture.</summary>
    GpuTexture? ILumOnProbeSh9GatherShaderProgramBindings.NearFieldRegions => NearFieldVisibility.Regions;
    #endregion
}
