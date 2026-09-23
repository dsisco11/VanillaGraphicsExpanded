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
public partial class LumOnProbeSh9GatherShaderProgram : LumOnShaderProgram
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
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    private LumOnProbeParamsUbo? paramsUbo;

    internal LumOnNearFieldVisibilityBindings NearFieldVisibility => ((LumOnProbeSh9GatherProgramLayout)ProgramLayout).NearFieldVisibility;

    protected override GpuProgramLayout CreateLayout() => new LumOnProbeSh9GatherProgramLayout();

    private LumOnProbeParamsUbo Params => paramsUbo ??= new LumOnProbeParamsUbo();

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
        var instance = new LumOnProbeSh9GatherShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };
        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram(Contract.Identity, instance);
    }

    #endregion

    #region SH9 Textures

    public GpuTexture? ProbeSh0 { set => BindTexture2D("probeSh0", value, 0); }
    public GpuTexture? ProbeSh1 { set => BindTexture2D("probeSh1", value, 1); }
    public GpuTexture? ProbeSh2 { set => BindTexture2D("probeSh2", value, 2); }
    public GpuTexture? ProbeSh3 { set => BindTexture2D("probeSh3", value, 3); }
    public GpuTexture? ProbeSh4 { set => BindTexture2D("probeSh4", value, 4); }
    public GpuTexture? ProbeSh5 { set => BindTexture2D("probeSh5", value, 5); }
    public GpuTexture? ProbeSh6 { set => BindTexture2D("probeSh6", value, 6); }

    #endregion

    #region Probe Anchors

    public GpuTexture? ProbeAnchorPosition { set => BindTexture2D("probeAnchorPosition", value, 7); }
    public GpuTexture? ProbeAnchorNormal { set => BindTexture2D("probeAnchorNormal", value, 8); }

    #endregion

    #region GBuffer Inputs

    public int PrimaryDepth { set => BindExternalTexture2D("primaryDepth", value, 9, GpuSamplers.NearestClamp); }
    public int GBufferNormal { set => BindExternalTexture2D("gBufferNormal", value, 10, GpuSamplers.NearestClamp); }

    #endregion

    // Per-frame state (matrices, sizes, probe grid params, zNear/zFar) is provided via LumOnFrameUBO.

    #region Uniforms

    public float Intensity
    {
        set
        {
            Params.Intensity = value;
            Params.BindTo(this, LumOnProbeParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public float[] IndirectTint
    {
        set
        {
            Params.IndirectTint = new System.Numerics.Vector3(value[0], value[1], value[2]);
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

    public GpuTexture? WorldProbeRadianceAtlas { set => BindTexture2D("worldProbeRadianceAtlas", value, 11); }
    public GpuTexture? WorldProbeVis0 { set => BindTexture2D("worldProbeVis0", value, 14); }
    public GpuTexture? WorldProbeMeta0 { set => BindTexture2D("worldProbeMeta0", value, 15); }

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
