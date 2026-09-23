using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

[ShaderProgram("Contract", "vge_worldprobe_orbs_points", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "vge_worldprobe_orbs_points.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "vge_worldprobe_orbs_points.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Orbs")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14)]
public sealed partial class VgeWorldProbeOrbsPointsShaderProgram : VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram
{
    #region Shader options
    /// <summary>Gets or sets the declared DirectVisibility shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.DirectVisibility))]
    public partial bool DirectVisibility { get; set; }

    /// <summary>Gets or sets the declared WorldProbeOctahedralSize shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeOctahedralSize))]
    public partial int WorldProbeOctahedralSize { get; set; }

    /// <summary>Gets or sets the declared WorldProbeResolution shader selection.</summary>
    [ShaderOptionReference(typeof(LumOnShaderOptions), nameof(LumOnShaderOptions.WorldProbeResolution))]
    public partial int WorldProbeResolution { get; set; }
    #endregion

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    private readonly VgeWorldProbeOrbsPointsParamsUbo paramsUbo = new();

    public VgeWorldProbeOrbsPointsShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    public static void Register(ICoreClientAPI api)
    {
        var instance = new VgeWorldProbeOrbsPointsShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };

        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram(Contract.Identity, instance);
    }

    public float[] ModelViewProjectionMatrix
    {
        set
        {
            paramsUbo.ModelViewProjectionMatrix = value;
            paramsUbo.BindTo(this, VgeWorldProbeOrbsPointsParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public Vec3f CameraPos
    {
        set
        {
            paramsUbo.CameraPos = value;
            paramsUbo.BindTo(this, VgeWorldProbeOrbsPointsParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public Vec3f WorldOffset
    {
        set
        {
            paramsUbo.WorldOffset = value;
            paramsUbo.BindTo(this, VgeWorldProbeOrbsPointsParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public float PointSize
    {
        set
        {
            paramsUbo.PointSize = value;
            paramsUbo.BindTo(this, VgeWorldProbeOrbsPointsParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public float FadeNear
    {
        set
        {
            paramsUbo.FadeNear = value;
            paramsUbo.BindTo(this, VgeWorldProbeOrbsPointsParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public float FadeFar
    {
        set
        {
            paramsUbo.FadeFar = value;
            paramsUbo.BindTo(this, VgeWorldProbeOrbsPointsParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public bool ImportanceColorMode { set => Uniform("importanceColorMode", value ? 1 : 0); }

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
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbeResolution, resolution);
        changed |= SetShaderOption(LumOnShaderOptions.WorldProbeOctahedralSize, worldProbeOctahedralTileSize);
        return !changed;
    }

    public int WorldProbeRadianceAtlas { set => Uniform("worldProbeRadianceAtlas", value); }
    public int WorldProbeVis0 { set => Uniform("worldProbeVis0", value); }
    public int WorldProbeDebugState0 { set => Uniform("worldProbeDebugState0", value); }
}
