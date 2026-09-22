using System;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

public sealed partial class VgeWorldProbeOrbsPointsShaderProgram : GpuProgram
{
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
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, resolution.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeOctahedralSize, worldProbeOctahedralTileSize.ToString(CultureInfo.InvariantCulture));
        return !changed;
    }

    public int WorldProbeRadianceAtlas { set => Uniform("worldProbeRadianceAtlas", value); }
    public int WorldProbeVis0 { set => Uniform("worldProbeVis0", value); }
    public int WorldProbeDebugState0 { set => Uniform("worldProbeDebugState0", value); }
}
