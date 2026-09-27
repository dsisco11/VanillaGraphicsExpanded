using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Links optional engine-owned GLSL tessellation without modifying the ordinary executable on failure.</summary>
internal static class TerrainTessellationLinker
{
    #region Candidate lifetime
    /// <summary>Returns an owned candidate using the engine's existing compiled vertex/fragment objects.</summary>
    internal static bool TryCreate(int vertex, int fragment, TerrainTessellationStages.Sources sources,
        string prefixCode, int level, out int program, out string error)
    {
        program = 0;
        error = "";
        GpuShaderModule? control = null, evaluation = null;
        int candidate = 0;
        try
        {
            if (level is < 1 or > 8) throw new ArgumentOutOfRangeException(nameof(level));
            // These pass-through stages use no SSBOs, even when the engine vertex stage does.
            // GLSL versions may differ between linked stages; retain the tessellation minimum here.
            string header = $$"""
                #version 400 core
                {{prefixCode}}
                #define VGE_TESSELLATION_LEVEL {{level}}

                """;
            if (!GpuShaderModule.TryCompileGlsl(ShaderType.TessControlShader, header + sources.Control, out control, out error)) return false;
            if (!GpuShaderModule.TryCompileGlsl(ShaderType.TessEvaluationShader, header + sources.Evaluation, out evaluation, out error)) return false;
            candidate = ShaderProgramLink.Submit([vertex, control!.ShaderId, evaluation!.ShaderId, fragment], false, out _);
            if (!ShaderProgramLink.Validate(candidate, out error)) return false;
            // The linked executable retains the stages; the engine continues owning vertex/fragment.
            GL.DetachShader(candidate, control.ShaderId);
            GL.DetachShader(candidate, evaluation.ShaderId);
            program = candidate; candidate = 0;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
        finally
        {
            if (candidate != 0) GL.DeleteProgram(candidate);
            control?.Dispose();
            evaluation?.Dispose();
        }
    }

    #endregion
}
