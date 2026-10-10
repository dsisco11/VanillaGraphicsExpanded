using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Maps declared stage kinds independently of source asset suffixes.</summary>
internal static class ShaderCompilerStage
{
    #region Public API
    /// <summary>Returns the layout suffix for a declared shader stage.</summary>
    internal static string Extension(ShaderStageKind kind) => kind switch
    {
        ShaderStageKind.Vertex => "vsh", ShaderStageKind.Fragment => "fsh", ShaderStageKind.Geometry => "gsh",
        ShaderStageKind.TessellationControl => "tcsh", ShaderStageKind.TessellationEvaluation => "tesh",
        ShaderStageKind.Compute => "csh", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    /// <summary>Returns the compiler's stage name directly from the declared kind.</summary>
    internal static string Name(ShaderStageKind kind) => kind switch
    {
        ShaderStageKind.Vertex => "vertex", ShaderStageKind.Fragment => "fragment", ShaderStageKind.Geometry => "geometry",
        ShaderStageKind.TessellationControl => "tesscontrol", ShaderStageKind.TessellationEvaluation => "tesseval",
        ShaderStageKind.Compute => "compute", _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };
    #endregion
}
