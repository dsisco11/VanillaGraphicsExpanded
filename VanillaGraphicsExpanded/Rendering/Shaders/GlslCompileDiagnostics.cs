using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

internal static class GlslCompileDiagnostics
{
    internal sealed class Result
    {
        public required bool Success { get; init; }
        public required string VertexLog { get; init; }
        public required string FragmentLog { get; init; }
        public required string GeometryLog { get; init; }
        public required string ProgramLog { get; init; }

        public bool HasAnyLog =>
            !string.IsNullOrWhiteSpace(VertexLog) ||
            !string.IsNullOrWhiteSpace(FragmentLog) ||
            !string.IsNullOrWhiteSpace(GeometryLog) ||
            !string.IsNullOrWhiteSpace(ProgramLog);

        public override string ToString()
        {
            return $"Success={Success}; vshLogLen={VertexLog?.Length ?? 0}; fshLogLen={FragmentLog?.Length ?? 0}; gshLogLen={GeometryLog?.Length ?? 0}; progLogLen={ProgramLog?.Length ?? 0}";
        }
    }

    public static Result TryCompile(string vertexSource, string fragmentSource, string? geometrySource = null)
    {
        // Best-effort: this helper is intended to run on the render thread (valid GL context).
        // Never throw from diagnostics.

        int vsh = 0;
        int fsh = 0;
        int gsh = 0;
        int program = 0;

        try
        {
            vsh = CompileShader(ShaderType.VertexShader, vertexSource ?? string.Empty, out var vLog, out var vOk);
            fsh = CompileShader(ShaderType.FragmentShader, fragmentSource ?? string.Empty, out var fLog, out var fOk);

            bool gOk = true;
            string gLog = string.Empty;
            if (!string.IsNullOrEmpty(geometrySource))
            {
                gsh = CompileShader(ShaderType.GeometryShader, geometrySource!, out gLog, out gOk);
            }

            bool ok = vOk && fOk && gOk;

            program = GL.CreateProgram();
            if (vsh != 0) GL.AttachShader(program, vsh);
            if (fsh != 0) GL.AttachShader(program, fsh);
            if (gsh != 0) GL.AttachShader(program, gsh);
            GL.LinkProgram(program);

            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linkStatus);
            string pLog = GL.GetProgramInfoLog(program) ?? string.Empty;

            // If compilation already failed, linking will likely fail as well.
            ok = ok && linkStatus != 0;

            return new Result
            {
                Success = ok,
                VertexLog = vLog,
                FragmentLog = fLog,
                GeometryLog = gLog,
                ProgramLog = pLog
            };
        }
        catch (Exception ex)
        {
            return new Result
            {
                Success = false,
                VertexLog = $"[VGE] Exception while compiling vertex shader for diagnostics: {ex}",
                FragmentLog = string.Empty,
                GeometryLog = string.Empty,
                ProgramLog = string.Empty
            };
        }
        finally
        {
            try
            {
                if (program != 0)
                {
                    if (vsh != 0) GL.DetachShader(program, vsh);
                    if (fsh != 0) GL.DetachShader(program, fsh);
                    if (gsh != 0) GL.DetachShader(program, gsh);
                    GL.DeleteProgram(program);
                }

                if (vsh != 0) GL.DeleteShader(vsh);
                if (fsh != 0) GL.DeleteShader(fsh);
                if (gsh != 0) GL.DeleteShader(gsh);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static int CompileShader(ShaderType type, string source, out string infoLog, out bool success)
    {
        int shader = 0;
        try
        {
            shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source ?? string.Empty);
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            infoLog = GL.GetShaderInfoLog(shader) ?? string.Empty;
            success = status != 0;

            return shader;
        }
        catch (Exception ex)
        {
            infoLog = $"[VGE] Exception while compiling {type} shader for diagnostics: {ex}";
            success = false;
            if (shader != 0)
            {
                try { GL.DeleteShader(shader); } catch { /* ignore */ }
            }
            return 0;
        }
    }
}
