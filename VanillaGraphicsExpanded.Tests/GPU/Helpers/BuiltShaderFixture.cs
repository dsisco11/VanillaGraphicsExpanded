using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Loads explicit registered stage identities through the strict production binary loader.</summary>
internal static class BuiltShaderFixture
{
    private static readonly ShaderVariantResolver Fixtures = ShaderBuildTool.Spirv.TestShaderPrograms.Create();

    #region Loading
    /// <summary>Loads a declared stage's requested selection and retains its numeric interface.</summary>
    public static int Load(string identity, ShaderType type, IReadOnlyDictionary<string, string?>? defines = null)
    {
        var stage = GpuShaderContracts.Registry.FindStage(identity);
        if (SpirvStageLoader.ToShaderType(stage.Kind) != type) throw new ArgumentException("Stage kind mismatch: " + identity);
        var owner = GpuShaderContracts.Registry.Programs.Values.OrderBy(p => p.Identity, StringComparer.Ordinal).First(p => p.Stages.Any(s => s.Identity == identity));
        var selected = new ShaderLoadPlan(new ShaderSettings(owner, defines)).Stages.Single(s => s.Stage.Identity == identity);
        var loaded = SpirvStageLoader.Load(selected,
            path => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path)));
        TestShaderInterfaces.TrackShader(loaded.Shader, loaded.Contract);
        return loaded.Shader;
    }
    /// <summary>Loads a fixed test stage with source-authored numeric locations and transfers its native ownership.</summary>
    public static int LoadFixture(string path, ShaderType type)
    {
        byte[] binary = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path + ".spv"));
        if (!VanillaGraphicsExpanded.Rendering.GpuShaderModule.TryLoadSpirv(type, binary, "main", [], out var module, out string error) || module == null)
            throw new InvalidOperationException(error);
        using (module) return (int)module.Detach();
    }
    /// <summary>Builds the numeric interface for fixture programs without relying on stripped binary names.</summary>
    public static VanillaGraphicsExpanded.Rendering.GpuProgramLayout Layout(int program, params string[] stages)
    {
        var layout = new VanillaGraphicsExpanded.Rendering.GpuProgramLayout
        {
            BinaryInterface = new GpuProgramInterface(program, stages.Select(path => Fixtures.FindStage(path).Bindings))
        };
        layout.RebuildCache(program);
        return layout;
    }
    #endregion
}
