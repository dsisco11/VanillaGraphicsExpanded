using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

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
        GpuProgramObject? program = null;
        int programId = 0;

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

            program = GpuProgramObject.Create(debugName: "vge_diag_compile_program");
            programId = program.ProgramId;

            if (vsh != 0) program.AttachShader(vsh);
            if (fsh != 0) program.AttachShader(fsh);
            if (gsh != 0) program.AttachShader(gsh);

            bool linkOk = program.TryLink(out string pLog);

            // If compilation already failed, linking will likely fail as well.
            ok = ok && linkOk;

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
                if (programId != 0)
                {
                    if (vsh != 0) program?.DetachShader(vsh);
                    if (fsh != 0) program?.DetachShader(fsh);
                    if (gsh != 0) program?.DetachShader(gsh);
                }

                if (vsh != 0) GL.DeleteShader(vsh);
                if (fsh != 0) GL.DeleteShader(fsh);
                if (gsh != 0) GL.DeleteShader(gsh);
            }
            catch
            {
                // ignore
            }
            finally
            {
                try { program?.Dispose(); } catch { }
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
