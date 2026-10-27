using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Integration.OpenGL;

/// <summary>Owns the explicit handoff between engine shader ownership and backend-created replacement programs.</summary>
internal static class OpenGLEngineShaderPrograms
{
    #region Internal API
    /// <summary>Installs a ready candidate after identity validation and reports post-install retirement failure separately from creation failure.</summary>
    internal static Exception? ReplaceWithTessellation(ShaderProgram program, RHIShaderStageDescription control,
        RHIShaderStageDescription evaluation, string? label)
    {
        ArgumentNullException.ThrowIfNull(program);
        var backend = RHICommandContext.Current.OpenGL;
        int original = program.ProgramId;
        var vertex = program.VertexShader;
        var fragment = program.FragmentShader;
        int vertexId = vertex.ShaderId, fragmentId = fragment.ShaderId;
        using var candidate = backend.CreateEngineTessellationProgram(vertexId, fragmentId, control, evaluation, label).RequireResource();
        backend.RequireContext();
        if (program.ProgramId != original || !ReferenceEquals(program.VertexShader, vertex) || !ReferenceEquals(program.FragmentShader, fragment)
            || vertex.ShaderId != vertexId || fragment.ShaderId != fragmentId)
            throw new InvalidOperationException("Engine shader replacement was superseded while its candidate was being built.");
        // The engine receives deletion ownership once. Candidate cleanup cannot retire the installed name.
        program.ProgramId = candidate.ProgramId;
        candidate.Detach();
        // Installation is committed. A cleanup diagnostic must not cause the caller to treat its new topology as absent.
        try { backend.DeleteProgram(original); return null; }
        catch (Exception error) { return error; }
    }
    #endregion
}
