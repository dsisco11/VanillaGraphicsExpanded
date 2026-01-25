using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

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
        GpuProgramObject? program = null;
        int programId = 0;

        try
        {
            program = GpuProgramObject.Create(debugName: "vge_diag_link_program");
            programId = program.ProgramId;

            if (vertexShaderId != 0) program.AttachShader(vertexShaderId);
            if (fragmentShaderId != 0) program.AttachShader(fragmentShaderId);
            if (geometryShaderId != 0) program.AttachShader(geometryShaderId);

            bool ok = program.TryLink(out string log);

            return new LinkDiagnostics
            {
                Success = ok,
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
                if (programId != 0)
                {
                    if (vertexShaderId != 0) program?.DetachShader(vertexShaderId);
                    if (fragmentShaderId != 0) program?.DetachShader(fragmentShaderId);
                    if (geometryShaderId != 0) program?.DetachShader(geometryShaderId);
                }
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
}
