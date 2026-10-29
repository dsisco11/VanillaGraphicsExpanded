using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Consumes executable factory results while their native names and output arrays remain valid.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    #region Shader and program factories
    /// <summary>Establishes fixed shader stage directly from a successful factory's input.</summary>
    [InterceptOpenGL("glCreateShader")]
    [InterceptOpenGL("glCreateShaderObjectARB")]
    internal static void ObserveCreateShader(int stage, int result)
    {
        if (result == 0 || !IsObservedShaderStage(stage) || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        RHIResourceManagerSystem.CurrentManager!.ObserveExecutableCreation(GpuResourceKind.Shader,
            unchecked((int)result), OpenGLShaderValues.Stage((ShaderType)stage));
    }

    /// <summary>Publishes the guaranteed empty unlinked interface without executable reflection.</summary>
    [InterceptOpenGL("glCreateProgram")]
    [InterceptOpenGL("glCreateProgramObjectARB")]
    internal static void ObserveCreateProgram(int result)
    {
        if (result == 0 || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        RHIResourceManagerSystem.CurrentManager!.ObserveExecutableCreation(GpuResourceKind.Program, unchecked((int)result));
    }

    /// <summary>Registers a combined factory's identity before completing its driver-produced executable output.</summary>
    [InterceptOpenGL("glCreateShaderProgramv")]
    internal static void ObserveCreateShaderProgramv(int stage, int count, nint strings, int result)
    {
        if (count < 0 || count > 0 && strings == 0 || !IsObservedShaderStage(stage)) return;
        ObserveCombinedProgram(result);
    }

    /// <summary>Retains desktop EXT single-source combined factory semantics.</summary>
    [InterceptOpenGL("glCreateShaderProgramEXT")]
    internal static void ObserveCreateShaderProgramEXT(int stage, nint source, int result)
    {
        if (source == 0 || !IsObservedShaderStage(stage)) return;
        ObserveCombinedProgram(result);
    }

    #endregion
    #region Pipeline factories
    /// <summary>Reserves generated pipeline names without claiming initialized object state.</summary>
    [InterceptOpenGL("glGenProgramPipelines")]
    internal static void ObserveGenProgramPipelines(int count, uint* names) => ObservePipelineNames(count, names, false);

    /// <summary>Establishes core-created pipeline defaults without selection queries.</summary>
    [InterceptOpenGL("glCreateProgramPipelines")]
    internal static void ObserveCreateProgramPipelines(int count, uint* names) => ObservePipelineNames(count, names, true);
    #endregion
    #endregion

    #region Private
    /// <summary>Rejects invalid factory stage tokens before admitting an output identity.</summary>
    private static bool IsObservedShaderStage(int stage) => stage is (int)ShaderType.VertexShader or (int)ShaderType.FragmentShader
        or (int)ShaderType.GeometryShader or (int)ShaderType.TessControlShader or (int)ShaderType.TessEvaluationShader or (int)ShaderType.ComputeShader;

    /// <summary>Completes only a new combined-factory allocation; a returned name alone never proves link success.</summary>
    private static void ObserveCombinedProgram(int result)
    {
        if (result == 0 || !OpenGLObservationContext.TryGet(out _, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        int id = unchecked((int)result);
        if (manager.FindResource(GpuResourceKind.Program, id) is not null) return;
        manager.ObserveExecutableCreation(GpuResourceKind.Program, id);
        ObserveProgramMetadata(id, true, false, -1, 1, executable: true);
    }

    /// <summary>Consumes names once and initializes execution-owned defaults only for realized new objects.</summary>
    private static void ObservePipelineNames(int count, uint* names, bool realized)
    {
        if (count <= 0 || names == null || !OpenGLObservationContext.TryGet(out var cache, out _, false)) return;
        var manager = RHIResourceManagerSystem.CurrentManager!;
        for (int i = 0; i < count; i++)
        {
            int id = unchecked((int)names[i]);
            if (id == 0 || manager.FindResource(GpuResourceKind.ProgramPipeline, id) is not null) continue;
            manager.ObserveExecutableCreation(GpuResourceKind.ProgramPipeline, id, realized: realized);
            if (realized) cache.RecordPipelineCreated(id);
        }
    }
    #endregion
}
