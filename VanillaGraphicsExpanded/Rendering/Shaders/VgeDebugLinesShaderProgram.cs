using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Minimal shader program for debug line rendering in clip space.
/// </summary>
public sealed class VgeDebugLinesShaderProgram : VgeShaderProgram
{
    public static void Register(ICoreClientAPI api)
    {
        var instance = new VgeDebugLinesShaderProgram
        {
            PassName = "vge_debug_lines",
            AssetDomain = "vanillagraphicsexpanded"
        };

        instance.Initialize(api);
        instance.CompileAndLink();
        api.Shader.RegisterMemoryShaderProgram("vge_debug_lines", instance);
    }

    public float[] ModelViewProjectionMatrix { set => UniformMatrix("modelViewProjectionMatrix", value); }
    public Vec3f WorldOffset { set => Uniform("worldOffset", value); }
}
