using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Stages;

internal static class ShaderProgramDiagnostics
{
    internal sealed class LinkDiagnostics
    {
        public required bool Success { get; init; }
        public required string ProgramInfoLog { get; init; }
    }

    public static LinkDiagnostics LinkDiagnosticsOnly(int vertexShaderId, int fragmentShaderId, int geometryShaderId = 0)
    {
        int program = 0;

        try
        {
            program = GL.CreateProgram();

            if (vertexShaderId != 0) GL.AttachShader(program, vertexShaderId);
            if (fragmentShaderId != 0) GL.AttachShader(program, fragmentShaderId);
            if (geometryShaderId != 0) GL.AttachShader(program, geometryShaderId);

            GL.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkStatus);
            string log = GL.GetProgramInfoLog(program) ?? string.Empty;

            return new LinkDiagnostics
            {
                Success = linkStatus != 0,
                ProgramInfoLog = log
            };
        }
        catch (Exception ex)
        {
            return new LinkDiagnostics
            {
                Success = false,
                ProgramInfoLog = $"[VGE] Exception while linking program for diagnostics: {ex}"
            };
        }
        finally
        {
            try
            {
                if (program != 0)
                {
                    if (vertexShaderId != 0) GL.DetachShader(program, vertexShaderId);
                    if (fragmentShaderId != 0) GL.DetachShader(program, fragmentShaderId);
                    if (geometryShaderId != 0) GL.DetachShader(program, geometryShaderId);

                    GL.DeleteProgram(program);
                }
            }
            catch
            {
                // ignore
            }
        }
    }
}
