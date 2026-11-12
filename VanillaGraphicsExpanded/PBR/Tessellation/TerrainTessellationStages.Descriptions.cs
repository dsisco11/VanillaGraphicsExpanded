using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Adapts generated terrain stage source to the existing engine compilation boundary.</summary>
internal static partial class TerrainTessellationStages
{
    #region Public API
    /// <summary>Freezes the selected engine prefix without issuing native shader creation or link commands.</summary>
    internal static (RHIShaderStageDescription Control, RHIShaderStageDescription Evaluation) Describe(Sources sources, string prefixCode)
    {
        string header = $"#version 400 core\n{prefixCode}\n\n";
        return (new(ShaderStage.TessellationControl, header + sources.Control),
            new(ShaderStage.TessellationEvaluation, header + sources.Evaluation));
    }
    #endregion
}
