using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Shares driver linking and failed-candidate cleanup between synchronous and deferred owners.</summary>
internal static class ShaderProgramLink
{
    #region Submission and status
    /// <summary>Creates and submits an executable, transferring ownership without querying completion.</summary>
    internal static int Submit(ReadOnlySpan<int> shaders, bool retrievableBinary, out double linkMilliseconds)
    {
        int program = GL.CreateProgram();
        if (program == 0) throw new InvalidOperationException("glCreateProgram returned 0.");
        try
        {
            DriverProgramCache.RequestRetrievable(program, retrievableBinary);
            foreach (int shader in shaders) GL.AttachShader(program, shader);
            long started = Stopwatch.GetTimestamp();
            GL.LinkProgram(program);
            linkMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return program;
        }
        catch { GL.DeleteProgram(program); throw; }
    }

    /// <summary>Checks final status; deferred callers must first establish extension completion.</summary>
    internal static bool Validate(int program, out string infoLog)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        infoLog = linked == 0 ? GL.GetProgramInfoLog(program) ?? string.Empty : string.Empty;
        return linked != 0;
    }
    #endregion
}
