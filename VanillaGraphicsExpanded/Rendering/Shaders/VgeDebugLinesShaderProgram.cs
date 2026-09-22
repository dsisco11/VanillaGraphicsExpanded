using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Minimal shader program for debug line rendering in clip space.
/// </summary>
public sealed class VgeDebugLinesShaderProgram : GpuProgram
{
    private readonly VgeDebugLinesParamsUbo paramsUbo = new();

    public VgeDebugLinesShaderProgram()
    {
        ProgramLayout.RegisterContract(global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Create("vge_debug_lines"));

    }

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

    public float[] ModelViewProjectionMatrix
    {
        set
        {
            paramsUbo.ModelViewProjectionMatrix = value;
            paramsUbo.BindTo(this, VgeDebugLinesParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }

    public Vec3f WorldOffset
    {
        set
        {
            paramsUbo.WorldOffset = value;
            paramsUbo.BindTo(this, VgeDebugLinesParamsUbo.BlockName, $"VGE.{ShaderName}.Params");
        }
    }
}
