using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Compiles or installs selected shader inputs and exposes only completed immutable executables.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<RHIShader> CreateShader(RHIShaderDescription description)
    {
        using var pending = BeginShaderCandidate(description);
        CompleteShaderCreation(pending, true);
        return pending.Result;
    }
    /// <inheritdoc />
    public RHIPendingShaderCreation BeginCreateShader(RHIShaderDescription description) => new(BeginShaderCandidate(description));

    #endregion

    #region Internal API
    /// <summary>Completes the original submitted candidate, preparing bindings only from its manager snapshot.</summary>
    internal CompletionStatus CompleteShaderCreation(OpenGLPendingShaderCreation request, bool wait)
    {
        if (request.Status != CompletionStatus.Pending) return request.Status;
        if (request.Transition) throw new InvalidOperationException("Shader creation is already in progress.");
        if (HasRetiredOrigin) { request.Cancel(); return request.Status; }
        RequireContext();
        request.Transition = true;
        try
        {
            var program = request.Program ?? throw new InvalidOperationException("The shader candidate has no executable.");
            program.RequireOriginalContext();
            ObjectDisposedException.ThrowIf(!program.IsValid, program);
            if (!wait)
            {
                var property = NativeCapabilities.SupportsArbParallelShaderCompile
                    ? (GetProgramParameterName)ArbParallelShaderCompile.CompletionStatusArb
                    : (GetProgramParameterName)KhrParallelShaderCompile.CompletionStatusKhr;
                GL.GetProgram(program.ProgramId, property, out int completed);
                RequireContext();
                if (completed == 0) return CompletionStatus.Pending;
            }
            // Driver status/log queries are output reads, not descriptor verification after every command.
            for (int index = 0; index < request.Stages.Count; index++)
            {
                var stage = request.Stages[index];
                GL.GetShader(stage.ShaderId, ShaderParameter.CompileStatus, out int compiled);
                RequireContext();
                if (compiled == 0)
                {
                    var input = request.Description.Stages[index];
                    string operation = input.Format == ShaderCodeFormat.Spirv ? "specialization" : "compilation";
                    string diagnostics = GL.GetShaderInfoLog(stage.ShaderId);
                    RequireContext();
                    return RejectShader(request, $"{input.Stage} shader {operation} failed: {diagnostics}");
                }
            }
            if (!CompleteProgramLink(program, out string log, request.Description.Stages.Select(stage => stage.Stage).ToArray()))
                return RejectShader(request, log);
            var metadata = GpuResourceManager.RequireProgramSnapshot(program);
            var graphics = request.Description.Kind == ShaderKind.Graphics ? new GraphicsExecutableInterface(metadata) : null;
            var prepared = new GpuProgramInterface(program.ProgramId, [request.Description.Bindings], graphics, metadata);
            foreach (var stage in request.Stages)
            {
                GL.DetachShader(program.ProgramId, stage.ShaderId);
                RequireContext();
                stage.Dispose();
            }
            request.Stages.Clear();
            var shader = new RHIShader(request.Description, program, prepared);
            request.Program = null;
            request.Result = new(shader, null);
            return request.Status = CompletionStatus.Ready;
        }
        catch (Exception error)
        {
            request.Error = error;
            request.Result = new(null, error.Message);
            request.Status = CompletionStatus.Failed;
            try { CleanupShaderCandidate(request); } catch { }
            throw;
        }
        finally { request.Transition = false; }
    }
    /// <summary>Releases unpublished ownership without issuing native cleanup into a foreign context.</summary>
    internal void ReleaseShaderCandidate(OpenGLPendingShaderCreation request)
    {
        if (request.Transition) throw new InvalidOperationException("Shader creation is already in progress.");
        request.Transition = true;
        try { CleanupShaderCandidate(request); }
        finally { request.Transition = false; }
    }
    #endregion

    #region Private
    /// <summary>Submits a backend-owned candidate without exposing native state through the request.</summary>
    private OpenGLPendingShaderCreation BeginShaderCandidate(RHIShaderDescription description)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(description);
        if (description.Binary is { } binary && !NativeCapabilities.ProgramBinaryFormats.Contains(binary.NativeFormat))
            throw new NotSupportedException("The selected executable binary format is unavailable on this context.");
        if (description.Binary is null && description.Stages.Any(stage => stage.Format == ShaderCodeFormat.Spirv)
            && !NativeCapabilities.SupportsArbGlSpirv)
            throw new NotSupportedException("The supplied SPIR-V stages require native SPIR-V ingestion support.");
        var request = new OpenGLPendingShaderCreation(this, description) { Transition = true };
        try
        {
            request.Program = CreateProgramOwner(description.Label);
            if (description.RetrievableBinary)
            {
                GL.ProgramParameter(request.Program.ProgramId, ProgramParameterName.ProgramBinaryRetrievableHint, 1);
                RequireContext();
            }
            if (description.Binary is null)
                foreach (var input in description.Stages)
                {
                    var stage = CreateShaderStage(input);
                    request.Stages.Add(stage);
                    GL.AttachShader(request.Program.ProgramId, stage.ShaderId);
                    RequireContext();
                }
            SubmitProgramExecutable(request.Program, description.Binary);
        }
        catch
        {
            try { CleanupShaderCandidate(request); } catch { }
            throw;
        }
        finally { request.Transition = false; }
        // Without completion-status support, linking is synchronous; no pretend nonblocking poll is exposed.
        if (!NativeCapabilities.SupportsArbParallelShaderCompile && !NativeCapabilities.SupportsKhrParallelShaderCompile)
            CompleteShaderCreation(request, true);
        return request;
    }

    /// <summary>Allocates and submits one selected stage while retaining ownership through exceptional cleanup.</summary>
    private GpuShaderModule CreateShaderStage(RHIShaderStageDescription input)
    {
        var stage = new GpuShaderModule();
        try
        {
            if (!GpuResourceManager.CreateShader(new() { Stage = (OpenTK.Graphics.OpenGL.ShaderType)NativeShaderStage(input.Stage) }, stage, out _).HasValue)
                throw new InvalidOperationException("Native shader stage allocation failed.");
            CompileShaderStage(stage, input);
            return stage;
        }
        catch { stage.Dispose(); throw; }
    }
    /// <summary>Translates the project stage kind explicitly.</summary>
    private static ShaderType NativeShaderStage(ShaderStage stage) => stage switch
    {
        ShaderStage.Vertex => ShaderType.VertexShader,
        ShaderStage.Fragment => ShaderType.FragmentShader,
        ShaderStage.Geometry => ShaderType.GeometryShader,
        ShaderStage.TessellationControl => ShaderType.TessControlShader,
        ShaderStage.TessellationEvaluation => ShaderType.TessEvaluationShader,
        ShaderStage.Compute => ShaderType.ComputeShader,
        _ => throw new ArgumentOutOfRangeException(nameof(stage))
    };
    /// <summary>Issues compilation or specialization once, leaving completion status to the candidate owner.</summary>
    private unsafe void CompileShaderStage(GpuShaderModule owner, RHIShaderStageDescription input)
    {
        if (input.Format == ShaderCodeFormat.Glsl)
        {
            GL.ShaderSource(owner.ShaderId, input.Source!);
            RequireContext();
            GL.CompileShader(owner.ShaderId);
        }
        else
        {
            fixed (byte* bytes = input.Binary.AsSpan())
                GL.ShaderBinary(1, [owner.ShaderId], ShaderBinaryFormat.ShaderBinaryFormatSpirV, (nint)bytes, input.Binary.Length);
            RequireContext();
            // The pinned signed overload preserves GLuint identifier/value bits without numeric conversion.
            int[] ids = input.Specializations.Select(value => unchecked((int)value.Id)).ToArray();
            int[] values = input.Specializations.Select(value => unchecked((int)value.Bits)).ToArray();
            GL.SpecializeShader(owner.ShaderId, input.EntryPoint, ids.Length, ids, values);
        }
        RequireContext();
    }
    /// <summary>Reports a detected native compile/link failure and retires only this unpublished candidate.</summary>
    private CompletionStatus RejectShader(OpenGLPendingShaderCreation request, string diagnostics)
    {
        request.Result = new(null, diagnostics);
        request.Status = CompletionStatus.Failed;
        CleanupShaderCandidate(request);
        return request.Status;
    }
    /// <summary>Preserves each owner until its original-context retirement has accepted cleanup.</summary>
    private static void CleanupShaderCandidate(OpenGLPendingShaderCreation request)
    {
        request.Program?.Dispose(); request.Program = null;
        for (int index = request.Stages.Count - 1; index >= 0; index--)
        {
            request.Stages[index].Dispose();
            request.Stages.RemoveAt(index);
        }
    }
    #endregion
}
