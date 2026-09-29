using System;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.PBR.Tessellation;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Retains engine-loaded source in memory for one bounded recovery of a patched program.</summary>
internal static class ShaderPatchRecovery
{
    private static readonly ConditionalWeakTable<ShaderProgram, Original> originals = new();

    /// <summary>Captures stage bodies before VGE publication; engine prefixes remain independently owned.</summary>
    private sealed record Original(string? Vertex, string? Fragment, string? Geometry);

    #region Source lifetime
    /// <summary>Starts a new recovery opportunity when the engine loads fresh source.</summary>
    internal static void Capture(ShaderProgram program)
    {
        originals.Remove(program);
        originals.Add(program, new(program.VertexShader?.Code, program.FragmentShader?.Code, program.GeometryShader?.Code));
    }

    /// <summary>Releases retained source when its engine owner is disposed.</summary>
    internal static void Forget(ShaderProgram program) => originals.Remove(program);
    #endregion

    #region Recovery
    /// <summary>Restores all stage interfaces together and retries only this program, at most once per source load.</summary>
    internal static bool TryRecover(ShaderProgram program, Func<bool> compile, Action<string> report, out bool result)
    {
        result = false;
        if (!originals.TryGetValue(program, out var source)) return false;
        // Consume before retry: nested compilation must never recursively recover a broken baseline.
        originals.Remove(program);
        report("Patched engine compilation failed; restoring the original in-memory source and retrying this program.");
        // The engine may query uniform locations after compilation fails, leaving GL errors
        // behind. Report those at the failed compilation boundary before a successful retry
        // can carry them into an unrelated render-stage error check.
        string errors = GlDebug.GetErrorsString("OpenGL errors observed after failed patched compilation");
        if (errors.Length != 0) report(errors);
        ShaderCapabilities.Forget(program);
        TerrainTessellationPrograms.Forget(program);
        if (program.VertexShader is not null) TerrainTessellationPatches.Forget(program.VertexShader);
        RetireStage(program.VertexShader);
        RetireStage(program.FragmentShader);
        RetireStage(program.GeometryShader);
        if (program.ProgramId != 0) GpuResource.DeleteOrEnqueue(GpuResourceKind.Program, program.ProgramId);
        program.ProgramId = 0;
        program.uniformLocations.Clear();
        if (program.VertexShader is not null) program.VertexShader.Code = source.Vertex!;
        if (program.FragmentShader is not null) program.FragmentShader.Code = source.Fragment!;
        if (program.GeometryShader is not null) program.GeometryShader.Code = source.Geometry!;
        result = compile();
        if (!result) report("Original engine shader compilation also failed; no further retry will be attempted.");
        return true;
    }

    /// <summary>Retires partial stage objects before the engine allocates replacements on retry.</summary>
    private static void RetireStage(Shader? shader)
    {
        if (shader is null || shader.ShaderId == 0) return;
        GpuResource.DeleteOrEnqueue(GpuResourceKind.Shader, shader.ShaderId);
        shader.ShaderId = 0;
    }
    #endregion
}
