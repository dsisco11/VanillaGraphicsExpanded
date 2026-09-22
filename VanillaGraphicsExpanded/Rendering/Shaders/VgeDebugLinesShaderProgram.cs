using VanillaGraphicsExpanded.Rendering.Contracts;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>
/// Minimal shader program for debug line rendering in clip space.
/// </summary>
[ShaderProgram("Contract", "vge_debug_lines", 1)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "vge_debug_lines.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "vge_debug_lines.fsh")]
public sealed partial class VgeDebugLinesShaderProgram : GpuProgram
{

    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override global::VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContract ProgramContract => Contract;

    private readonly VgeDebugLinesParamsUbo paramsUbo = new();

    public VgeDebugLinesShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);

    }

    public static void Register(ICoreClientAPI api)
    {
        var instance = new VgeDebugLinesShaderProgram
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
