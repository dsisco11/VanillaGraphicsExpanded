using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

public sealed class VgeWorldProbeOrbsPointsShaderProgram : VgeShaderProgram
{
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

    public float[] ModelViewProjectionMatrix { set => UniformMatrix("modelViewProjectionMatrix", value); }
    public float[] InvViewMatrix { set => UniformMatrix("invViewMatrix", value); }
    public Vec3f CameraPos { set => Uniform("cameraPos", value); }
    public Vec3f WorldOffset { set => Uniform("worldOffset", value); }
    public float PointSize { set => Uniform("pointSize", value); }
    public float FadeNear { set => Uniform("fadeNear", value); }
    public float FadeFar { set => Uniform("fadeFar", value); }

    public int WorldProbeSH0 { set => Uniform("worldProbeSH0", value); }
    public int WorldProbeSH1 { set => Uniform("worldProbeSH1", value); }
    public int WorldProbeSH2 { set => Uniform("worldProbeSH2", value); }
    public int WorldProbeSky0 { set => Uniform("worldProbeSky0", value); }
    public int WorldProbeVis0 { set => Uniform("worldProbeVis0", value); }

    public Vec3f WorldProbeSkyTint { set => Uniform("worldProbeSkyTint", value); }
}
