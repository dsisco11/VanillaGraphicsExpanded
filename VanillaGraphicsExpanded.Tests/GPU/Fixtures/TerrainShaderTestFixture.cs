using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.PBR.Tessellation;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns runtime terrain test modules and retains their CPU source for interface preparation.</summary>
internal sealed class TerrainShaderTestFixture : IDisposable
{
    private readonly Dictionary<int, (GpuShaderModule Module, string Source)> stages = new();

    #region Shader lifecycle
    /// <summary>Exercises the backend's explicit borrowed-stage integration using the same CPU source policy as engine replacement.</summary>
    internal static bool LinkTessellation(int vertex, int fragment, TerrainTessellationStages.Sources sources,
        string prefix, out int program, out string error)
    {
        program = 0;
        error = string.Empty;
        try
        {
            var stages = TerrainTessellationStages.Describe(sources, prefix);
            var result = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRHIContext.CreateEngineTessellationProgram(vertex, fragment, stages.Control, stages.Evaluation, null);
            if (!result.TryGetResource(out var owner)) { error = result.Diagnostics ?? "Tessellation linking failed."; return false; }
            using (owner) program = (int)owner!.Detach();
            return true;
        }
        catch (Exception failure) { error = failure.Message; return false; }
    }

    /// <summary>Compiles through the production module abstraction and owns the result until fixture disposal.</summary>
    internal int Compile(ShaderType type, string source)
    {
        var module = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRHIContext.CompileExternalStage(new(PrecompiledStageFixture.Stage(type), source)).RequireResource();
        stages.Add(module!.ShaderId, (module, source));
        return module.ShaderId;
    }

    /// <summary>Owns a precompiled reusable fixture alongside runtime-generated test stages.</summary>
    internal int Load(ShaderType type, string path)
    {
        byte[] binary = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path + ".spv"));
        var module = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRHIContext.CompileExternalStage(new(PrecompiledStageFixture.Stage(type), binary)).RequireResource();
        stages.Add(module!.ShaderId, (module, string.Empty));
        return module.ShaderId;
    }

    /// <summary>Returns the exact submitted source without querying the driver.</summary>
    internal string Source(int shader) => stages[shader].Source;

    /// <summary>Links a baseline executable and transfers its ownership to the caller or engine owner.</summary>
    internal static int Link(int vertex, int fragment)
    {
        using var program = NativeResourceSetup.CreateProgram();
        GL.AttachShader(program.ProgramId, vertex);
        GL.AttachShader(program.ProgramId, fragment);
        Assert.True(NativeProgramFixture.Link(program, out string error), error);
        return (int)program.Detach();
    }

    /// <summary>Releases every stage even when compilation, linking or an assertion fails.</summary>
    public void Dispose()
    {
        foreach (var stage in stages.Values) stage.Module.Dispose();
        stages.Clear();
    }
    #endregion
}
