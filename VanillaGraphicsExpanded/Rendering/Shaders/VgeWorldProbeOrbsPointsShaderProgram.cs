using System;
using System.Globalization;

using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

public sealed class VgeWorldProbeOrbsPointsShaderProgram : GpuProgram
{
    private readonly VgeWorldProbeOrbsPointsParamsUbo paramsUbo = new();

    public VgeWorldProbeOrbsPointsShaderProgram()
    {
        RegisterUniformBlockBinding(LumOnUniformBuffers.FrameBlockName, LumOnUniformBuffers.FrameBinding, required: true);
        RegisterUniformBlockBinding(LumOnUniformBuffers.WorldProbeBlockName, LumOnUniformBuffers.WorldProbeBinding, required: true);
        RegisterUniformBlockBinding(VgeWorldProbeOrbsPointsParamsUbo.BlockName, GpuBindingRegistry.Ubo.Object, required: true);
    }

    public static void Register(ICoreClientAPI api)
    {
        var instance = new VgeWorldProbeOrbsPointsShaderProgram
        {
            PassName = "vge_worldprobe_orbs_points",
            AssetDomain = "vanillagraphicsexpanded"
        };

        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("vge_worldprobe_orbs_points", instance);
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
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, enabled ? "1" : "0");
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, levels.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, resolution.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, baseSpacing.ToString("0.0####", CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeOctahedralSize, worldProbeOctahedralTileSize.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeAtlasTexelsPerUpdate, worldProbeAtlasTexelsPerUpdate.ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeDiffuseStride, Math.Max(1, worldProbeDiffuseStride).ToString(CultureInfo.InvariantCulture));
        changed |= SetDefine(VgeShaderDefines.LumOnWorldProbeBindRadianceAtlas, enabled ? "1" : "0");
        return !changed;
    }

    public int WorldProbeRadianceAtlas { set => Uniform("worldProbeRadianceAtlas", value); }
    public int WorldProbeVis0 { set => Uniform("worldProbeVis0", value); }
    public int WorldProbeDebugState0 { set => Uniform("worldProbeDebugState0", value); }
}
