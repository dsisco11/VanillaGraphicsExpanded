using System;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Exposes named native shader integration operations without granting backend access to consumers.</summary>
internal static partial class OpenGLRHIContext
{
    #region Internal API
    /// <summary>Publishes an injected engine uniform slot under engine-owned membership.</summary>
    internal static bool BindEngineUniform(int slot, RHIUniformSnapshot snapshot)
    { _ = Current; return backend!.BindEngineUniform(slot, snapshot); }
    /// <summary>Builds and installs a native engine executable under the engine's ownership contract.</summary>
    internal static Exception? ReplaceEngineTessellationProgram(Vintagestory.Client.NoObf.ShaderProgram program,
        RHIShaderStageDescription control, RHIShaderStageDescription evaluation, string? label)
    { _ = Current; return backend!.ReplaceEngineTessellationProgram(program, control, evaluation, label); }
    /// <summary>Creates one native stage for explicit external executable integration.</summary>
    internal static RHICreationResult<GpuShaderModule> CompileExternalStage(RHIShaderStageDescription input)
    { _ = Current; return backend!.CompileExternalStage(input); }
    /// <summary>Creates an engine-owned native tessellation candidate from borrowed stages.</summary>
    internal static RHICreationResult<GpuProgramObject> CreateEngineTessellationProgram(int vertex, int fragment,
        RHIShaderStageDescription control, RHIShaderStageDescription evaluation, string? label)
    { _ = Current; return backend!.CreateEngineTessellationProgram(vertex, fragment, control, evaluation, label); }
    #endregion
}
