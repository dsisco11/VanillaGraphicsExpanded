using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Specialized shader program for PBR height-bake passes.
/// Automatically registers the shared VgePbrHeightBakeParamsUBO binding.
/// </summary>
internal sealed class PbrHeightBakeShaderProgram : VgeStageNamedShaderProgram
{
    private const string ParamsBlockName = "VgePbrHeightBakeParamsUBO";

    public PbrHeightBakeShaderProgram(string passName, string vertexStageName, string fragmentStageName, string domain)
        : base(passName, vertexStageName, fragmentStageName, domain)
    {
        RegisterUniformBlockBinding(ParamsBlockName, GpuBindingRegistry.Ubo.Object, required: true);
    }
}
