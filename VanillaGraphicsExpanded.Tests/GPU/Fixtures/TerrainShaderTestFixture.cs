using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns runtime terrain test modules and retains their CPU source for interface preparation.</summary>
internal sealed class TerrainShaderTestFixture : IDisposable
{
    private readonly Dictionary<int, (GpuShaderModule Module, string Source)> stages = new();

    #region Shader lifecycle
    /// <summary>Compiles through the production module abstraction and owns the result until fixture disposal.</summary>
    internal int Compile(ShaderType type, string source)
    {
        Assert.True(GpuShaderModule.TryCompileGlsl(type, source, out var module, out string error), error);
        stages.Add(module!.ShaderId, (module, source));
        return module.ShaderId;
    }

    /// <summary>Owns a precompiled reusable fixture alongside runtime-generated test stages.</summary>
    internal int Load(ShaderType type, string path)
    {
        byte[] binary = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path + ".spv"));
        Assert.True(GpuShaderModule.TryLoadSpirv(type, binary, "main", [], out var module, out string error), error);
        stages.Add(module!.ShaderId, (module, string.Empty));
        return module.ShaderId;
    }

    /// <summary>Returns the exact submitted source without querying the driver.</summary>
    internal string Source(int shader) => stages[shader].Source;

    /// <summary>Links a baseline executable and transfers its ownership to the caller or engine owner.</summary>
    internal static int Link(int vertex, int fragment)
    {
        using var program = GpuProgramObject.Create();
        program.AttachShader(vertex);
        program.AttachShader(fragment);
        Assert.True(program.TryLink(out string error), error);
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
