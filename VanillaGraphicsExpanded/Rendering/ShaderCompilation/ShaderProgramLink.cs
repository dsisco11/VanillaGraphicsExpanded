using System;
using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Shares driver linking and failed-candidate cleanup between synchronous and deferred owners.</summary>
internal static class ShaderProgramLink
{
    #region Submission and status
    /// <summary>Retains the common resource owner while compilation runs, without querying link completion.</summary>
    internal static GpuProgramObject Submit(ReadOnlySpan<int> shaders, bool retrievableBinary
#if DEBUG
        , out double linkMilliseconds
#endif
        )
    {
        var program = GpuProgramObject.Create();
        try
        {
            DriverProgramCache.RequestRetrievable(program.ProgramId, retrievableBinary);
            foreach (int shader in shaders) program.AttachShader(shader);
#if DEBUG
            long started = Stopwatch.GetTimestamp();
#endif
            StateCache.Current.SubmitProgramLink(program);
#if DEBUG
            linkMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
#endif
            return program;
        }
        catch { program.Dispose(); throw; }
    }

    /// <summary>Completes the pending original allocation after its owner establishes driver completion.</summary>
    internal static bool Validate(GpuProgramObject program, out string infoLog)
        => StateCache.Current.CompleteProgramLink(program, out infoLog);
    #endregion
}
