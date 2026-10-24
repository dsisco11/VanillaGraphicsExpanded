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

    /// <summary>Checks final status; deferred callers must first establish extension completion.</summary>
    internal static bool Validate(int program, out string infoLog)
    {
        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
        infoLog = linked == 0 ? GL.GetProgramInfoLog(program) ?? string.Empty : string.Empty;
        StateCache.Current.CompleteProgramLink(program);
        return linked != 0;
    }

    /// <summary>Validates the original owned allocation and publishes context-local facts after completion.</summary>
    internal static bool Validate(GpuProgramObject program, out string infoLog)
    {
        if (!program.IsValid) { infoLog = "The program candidate has retired."; return false; }
        bool linked = Validate(program.ProgramId, out infoLog);
        if (program.ResourceManager?.GetResourceRegistration(program) is null)
        {
            const GpuMetadataChange change = GpuMetadataChange.Metadata | GpuMetadataChange.Executable;
            GpuResourceManager.ObserveWrapperMetadata(program, GpuProgramMetadataReader.Instance, change);
            program.OnNativeProgramMetadataChanged(change, available: true);
        }
        return linked;
    }
    #endregion
}
