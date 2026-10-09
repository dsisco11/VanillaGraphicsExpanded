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
public sealed partial class VgeDebugLinesShaderProgram : GpuProgram, IVgeDebugLinesShaderProgramBindings
{



    /// <summary>Uses the immutable declaration owned by this shader class.</summary>
    internal override GpuShaderContract ProgramContract => Contract;

    private readonly VgeDebugLinesParamsUbo paramsUbo = new();
    private VgeFrameUniformBuffer? frameInputs;
    /// <summary>Supplies an explicit view snapshot or reuses the shared world view.</summary>
    internal VgeFrameUniformBuffer? FrameInputs
    {
        get => frameInputs;
        set { RequireInputMutation(); frameInputs = value; }
    }

    /// <summary>Registers the draw contract and guards retained parameter writes.</summary>
    public VgeDebugLinesShaderProgram()
    {
        foreach (var stage in Contract.Stages) ProgramLayout.RegisterContract(stage.Bindings);
        paramsUbo.SetWriteGuard(RequireInputMutation);

    }

    /// <summary>Declares the debug pass for demand preparation.</summary>
    public static void Register(ICoreClientAPI api)
    {
        var instance = new VgeDebugLinesShaderProgram
        {
            PassName = Contract.Identity,
            AssetDomain = "vanillagraphicsexpanded"
        };

        global::VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderPrograms.Declare(api, instance);
    }

    /// <summary>Offsets the line geometry within the shared camera-relative space.</summary>
    public Vec3f WorldOffset
    {
        set
        {
            paramsUbo.WorldOffset = value;
        }
    }
    /// <summary>Reuses the shared camera publication for line projection.</summary>
    CpuUniformBuffer IVgeDebugLinesShaderProgramBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Supplies retained packed parameters for generated submission.</summary>
    CpuUniformBuffer IVgeDebugLinesShaderProgramBindings.Parameters => paramsUbo;
}
