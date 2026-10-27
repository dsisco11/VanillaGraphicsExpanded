using System;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes the fixed stage of a new shader, independently of compilation inputs.</summary>
internal struct GpuShaderInitialization
{
    private ShaderStage stage;
    private byte assigned;

    #region Public API
    /// <summary>Gets or explicitly assigns the required shader stage.</summary>
    public ShaderStage Stage { readonly get => stage; set { stage = value; assigned = 1; } }
    #endregion

    #region Internal API
    /// <summary>Rejects missing or unsupported stages before native allocation.</summary>
    internal readonly void Validate()
    {
        if (assigned == 0) throw new ArgumentException("Shader stage is required.");
        bool supported = stage switch
        {
            // All programmable stages are part of the admitted desktop 4.5 baseline.
            ShaderStage.Vertex or ShaderStage.Fragment or ShaderStage.Geometry
                or ShaderStage.TessellationControl or ShaderStage.TessellationEvaluation or ShaderStage.Compute => true,
            _ => false
        };
        if (!supported) throw new NotSupportedException("The requested shader stage is unavailable.");
    }
    #endregion
}
