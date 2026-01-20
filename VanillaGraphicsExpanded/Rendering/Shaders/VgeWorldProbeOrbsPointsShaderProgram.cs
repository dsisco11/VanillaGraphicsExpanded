using Vintagestory.API.Client;

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
    public float PointSize { set => Uniform("pointSize", value); }

    public int WorldProbeSH0 { set => Uniform("worldProbeSH0", value); }
    public int WorldProbeSH1 { set => Uniform("worldProbeSH1", value); }
    public int WorldProbeSH2 { set => Uniform("worldProbeSH2", value); }
}

