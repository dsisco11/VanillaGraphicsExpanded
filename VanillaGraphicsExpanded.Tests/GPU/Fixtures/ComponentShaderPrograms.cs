using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns production shader instances for isolated pass tests, using the built binary assets.</summary>
internal sealed class ComponentShaderPrograms : IDisposable
{
    private readonly BinaryShaderApiFixture assets = new();
    private readonly EngineShaderPlatformScope platform = new();
    private readonly List<GpuProgram> programs = [];

    #region Program lifetime
    /// <summary>Configures a real shader before loading its declared variant; retains ownership through test teardown.</summary>
    public T Create<T>(Action<T>? configure = null, IReadOnlyDictionary<string, string?>? settings = null, string? identity = null) where T : GpuProgram, new()
    {
        // Nested engine fixtures may have released the import resolver since the previous draw.
        VanillaGraphicsExpanded.PBR.ShaderImportsSystem.Instance.Initialize(assets.Api);
        var program = new T();
        programs.Add(program);
        program.PassName = identity ?? program.ProgramContract.Identity;
        program.AssetDomain = "vanillagraphicsexpanded";
        // Supply the engine's stage objects without starting its window or renderer.
        program.VertexShader = new Shader();
        program.FragmentShader = new Shader();
        if (settings != null)
            foreach (var setting in settings) program.SetDefine(setting.Key, setting.Value);
        configure?.Invoke(program);
        program.Initialize(assets.Api);
        Assert.True(program.CompileAndLink(), string.Join(Environment.NewLine, assets.Logs));
        return program;
    }

    /// <summary>Releases programs while their engine boundary and graphics context are still available.</summary>
    public void Dispose()
    {
        foreach (var program in programs) program.Dispose();
        programs.Clear();
        platform.Dispose();
        assets.Dispose();
    }
    #endregion
}
