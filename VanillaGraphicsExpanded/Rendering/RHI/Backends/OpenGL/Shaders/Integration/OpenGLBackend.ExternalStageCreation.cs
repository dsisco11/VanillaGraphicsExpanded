using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates explicit native stages for engine integration without exposing portable stage ownership APIs.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Returns a completed integration-owned stage or native compiler diagnostics, with candidate-only cleanup.</summary>
    internal RHICreationResult<GpuShaderModule> CompileExternalStage(RHIShaderStageDescription input)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(input);
        if (input.Format == ShaderCodeFormat.Spirv && !NativeCapabilities.SupportsArbGlSpirv)
            throw new NotSupportedException("The selected native stage requires SPIR-V ingestion support.");
        var stage = CreateShaderStage(input);
        try
        {
            GL.GetShader(stage.ShaderId, ShaderParameter.CompileStatus, out int compiled);
            RequireContext();
            if (compiled == 0)
            {
                string diagnostics = GL.GetShaderInfoLog(stage.ShaderId);
                RequireContext();
                return new(null, diagnostics);
            }
            var ready = stage;
            stage = null;
            return new(ready, null);
        }
        finally { stage?.Dispose(); }
    }
    #endregion
}
