using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Creates engine executables from explicitly borrowed native stages and selected additional stage code.</summary>
internal sealed partial class OpenGLBackend
{
    #region Internal API
    /// <summary>Links an owned tessellation candidate while preserving engine ownership of vertex and fragment stages.</summary>
    internal RHICreationResult<GpuProgramObject> CreateEngineTessellationProgram(int vertex, int fragment,
        RHIShaderStageDescription controlInput, RHIShaderStageDescription evaluationInput, string? label)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(controlInput);
        ArgumentNullException.ThrowIfNull(evaluationInput);
        if (vertex <= 0 || fragment <= 0) throw new ArgumentException("Borrowed engine vertex and fragment stages are required.");
        if (controlInput.Stage != ShaderStage.TessellationControl || evaluationInput.Stage != ShaderStage.TessellationEvaluation
            || controlInput.Format != ShaderCodeFormat.Glsl || evaluationInput.Format != ShaderCodeFormat.Glsl)
            throw new ArgumentException("Engine tessellation integration requires selected control and evaluation GLSL stages.");
        GpuProgramObject? candidate = null;
        using var control = CreateShaderStage(controlInput);
        using var evaluation = CreateShaderStage(evaluationInput);
        try
        {
            foreach (var stage in new[] { control, evaluation })
            {
                GL.GetShader(stage.ShaderId, ShaderParameter.CompileStatus, out int compiled);
                RequireContext();
                if (compiled == 0)
                {
                    string stageDiagnostics = GL.GetShaderInfoLog(stage.ShaderId);
                    RequireContext();
                    return new(null, stageDiagnostics);
                }
            }
            candidate = GpuProgramObject.Create(label);
            foreach (int stage in new[] { vertex, control.ShaderId, evaluation.ShaderId, fragment })
            {
                GL.AttachShader(candidate.ProgramId, stage);
                RequireContext();
            }
            SubmitProgramExecutable(candidate);
            if (!CompleteProgramLink(candidate, out string diagnostics,
                [ShaderStage.Vertex, ShaderStage.TessellationControl, ShaderStage.TessellationEvaluation, ShaderStage.Fragment]))
                return new(null, diagnostics);
            // Only temporary VGE stages are detached and retired. Engine stages remain engine-owned.
            GL.DetachShader(candidate.ProgramId, control.ShaderId);
            RequireContext();
            GL.DetachShader(candidate.ProgramId, evaluation.ShaderId);
            RequireContext();
            var ready = candidate;
            candidate = null;
            return new(ready, null);
        }
        finally { candidate?.Dispose(); }
    }
    #endregion
}
