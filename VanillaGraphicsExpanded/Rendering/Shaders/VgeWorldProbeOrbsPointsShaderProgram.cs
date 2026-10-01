using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using System;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Retains world-probe debug draw inputs for generated submission.</summary>
[ShaderProgram("Contract", "vge_worldprobe_orbs_points", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "vge_worldprobe_orbs_points.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "vge_worldprobe_orbs_points.fsh")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Visibility")]
[ShaderAcceptGroup("Contract", typeof(LumOnShaderGroups), "Orbs")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(DirectVisibility))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeOctahedralSize), SpecializationId = 13)]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(WorldProbeResolution), SpecializationId = 14)]
public sealed partial class VgeWorldProbeOrbsPointsShaderProgram : VanillaGraphicsExpanded.LumOn.Shaders.LumOnShaderProgram, IVgeWorldProbeOrbsPointsShaderProgramBindings
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
    internal override GpuShaderContract ProgramContract => Contract;

    private readonly VgeWorldProbeOrbsPointsParamsUbo paramsUbo = new();

    /// <summary>Registers the draw contract and guards retained parameter writes.</summary>
    public VgeWorldProbeOrbsPointsShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
        paramsUbo.SetWriteGuard(RequireInputMutation);
    }

    /// <summary>Declares the debug pass for demand preparation.</summary>
    public static void Register(ICoreClientAPI api)
    {
        var instance = new VgeWorldProbeOrbsPointsShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };

        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    public float[] ModelViewProjectionMatrix
    {
        set
        {
            paramsUbo.ModelViewProjectionMatrix = value;
        }
    }

    public Vec3f CameraPos
    {
        set
        {
            paramsUbo.CameraPos = value;
        }
    }

    public Vec3f WorldOffset
    {
        set
        {
            paramsUbo.WorldOffset = value;
        }
    }

    public float PointSize
    {
        set
        {
            paramsUbo.PointSize = value;
        }
    }

    public float FadeNear
    {
        set
        {
            paramsUbo.FadeNear = value;
        }
    }

    public float FadeFar
    {
        set
        {
            paramsUbo.FadeFar = value;
        }
    }

    public bool ImportanceColorMode { set => paramsUbo.ImportanceColorMode = value; }

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
            options.Set(LumOnShaderOptions.WorldProbeResolution, resolution);
            options.Set(LumOnShaderOptions.WorldProbeOctahedralSize, worldProbeOctahedralTileSize);
        });
        return !changed;
    }

    public partial GpuTexture? WorldProbeRadianceAtlas { set; }
    public partial GpuTexture? WorldProbeVis0 { set; }
    public partial GpuTexture? WorldProbeDebugState0 { set; }
    /// <summary>Supplies retained packed parameters for generated submission.</summary>
    CpuUniformBuffer IVgeWorldProbeOrbsPointsShaderProgramBindings.Parameters => paramsUbo;
    /// <summary>Supplies shared frame storage.</summary>
    GpuUniformBuffer? IVgeWorldProbeOrbsPointsShaderProgramBindings.LumOnFrame => RetainedFrame;
    /// <summary>Supplies shared world-probe storage.</summary>
    GpuUniformBuffer? IVgeWorldProbeOrbsPointsShaderProgramBindings.LumOnWorldProbe => RetainedWorldProbe;
}
