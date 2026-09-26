using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Owns an unpublished program and its stages between driver submission and completed consumption.</summary>
internal sealed class PendingShaderProgram : IDisposable
{
    internal int Program { get; private set; }
    internal PreparedProgramBinary Inputs { get; }
    internal bool Cached { get; }
    internal double LinkSubmissionMilliseconds { get; }
    internal bool Validated => completed;
    internal List<(ShaderStageKind Kind, int Shader, GpuBindingContract Contract)> Stages { get; } = new();
    private readonly GetProgramParameterName completionQuery;
    private readonly ShaderLoadPlan plan;
    private bool completed;

    #region Submission and completion
    /// <summary>Submits specialization and linking without status, log or interface queries on cache misses.</summary>
    internal PendingShaderProgram(ShaderLoadPlan plan, ShaderAssetReader read,
        Func<ShaderBinaryDigest.Manifest?> digestIndex, GetProgramParameterName completionQuery)
    {
        this.completionQuery = completionQuery;
        this.plan = plan;
        Inputs = DriverProgramCache.Prepare(plan, read, digestIndex);
        try
        {
            Program = GL.CreateProgram();
            if (Program == 0) throw new InvalidOperationException("glCreateProgram returned 0.");
            Cached = DriverProgramCache.TryLoad(Program, Inputs);
            if (Cached) { completed = true; return; }
            GL.DeleteProgram(Program);
            Program = 0;
            foreach (var selection in plan.Stages)
            {
                var stage = SpirvStageLoader.Load(selection, read: Inputs.Read, deferCompletion: true);
                Stages.Add((selection.Stage.Kind, stage.Shader, stage.Contract));
            }
            Program = ShaderProgramLink.Submit(Stages.Select(stage => stage.Shader).ToArray(), Inputs.Key != null, out double elapsed);
            LinkSubmissionMilliseconds = elapsed;
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Checks only the extension's nonblocking completion property.</summary>
    internal bool IsComplete
    {
        get
        {
            if (completed) return true;
            GL.GetProgram(Program, completionQuery, out int ready);
            return ready != 0;
        }
    }

    /// <summary>Waits at an actual dependency, then validates every submitted stage before allowing publication.</summary>
    internal void Complete(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (completed) return;
        long started = Stopwatch.GetTimestamp();
        while (!IsComplete)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(60))
                throw new TimeoutException($"Driver compilation for {plan.Settings.Contract.Identity} did not complete within 60 seconds.");
            Thread.Yield();
        }
        cancellationToken.ThrowIfCancellationRequested();
        // Status and diagnostic queries are safe only after linking has completed.
        foreach (var stage in Stages)
        {
            GL.GetShader(stage.Shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0) throw new InvalidOperationException($"SPIR-V specialization failed for {plan.Settings.Contract.Identity} ({stage.Kind}): " + GL.GetShaderInfoLog(stage.Shader));
        }
        if (!ShaderProgramLink.Validate(Program, out string log)) throw new InvalidOperationException($"SPIR-V link failed for {plan.Settings.Contract.Identity}: " + log);
        completed = true;
    }

    /// <summary>Transfers a completed executable; stage ownership remains here until explicitly moved or disposed.</summary>
    internal int DetachProgram()
    {
        if (!completed) throw new InvalidOperationException("Cannot publish an unfinished shader program.");
        int result = Program;
        Program = 0;
        return result;
    }
    #endregion

    #region Lifetime
    /// <summary>Deletes unpublished executables and any stages not transferred to a graphics owner.</summary>
    public void Dispose()
    {
        if (Program != 0) { GL.DeleteProgram(Program); Program = 0; }
        foreach (var stage in Stages) GL.DeleteShader(stage.Shader);
        Stages.Clear();
    }
    #endregion
}
