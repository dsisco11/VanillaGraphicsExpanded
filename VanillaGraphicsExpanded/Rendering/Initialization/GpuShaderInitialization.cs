using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Describes the fixed stage of a new shader, independently of compilation inputs.</summary>
internal struct GpuShaderInitialization
{
    private ShaderType stage;
    private byte assigned;

    #region Public API
    /// <summary>Gets or explicitly assigns the required shader stage.</summary>
    public ShaderType Stage { readonly get => stage; set { stage = value; assigned = 1; } }
    #endregion

    #region Internal API
    /// <summary>Rejects missing or unsupported stages before native allocation.</summary>
    internal readonly void Validate()
    {
        if (assigned == 0) throw new ArgumentException("Shader stage is required.");
        var capabilities = GpuSupport.Graphics;
        bool supported = stage switch
        {
            ShaderType.VertexShader or ShaderType.FragmentShader or ShaderType.GeometryShader => true,
            ShaderType.TessControlShader or ShaderType.TessEvaluationShader => capabilities.Tessellation,
            ShaderType.ComputeShader => capabilities.SupportsArbComputeShader,
            _ => false
        };
        if (!supported) throw new NotSupportedException("The requested shader stage is unavailable.");
    }
    #endregion
}
