using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Loads explicit registered stage identities through the strict production binary loader.</summary>
internal static class BuiltShaderFixture
{
    #region Loading
    /// <summary>Loads a declared stage's default selection and retains its numeric interface.</summary>
    public static int Load(string identity, ShaderType type)
    {
        var stage = GpuShaderContracts.Registry.FindStage(identity);
        if (SpirvStageLoader.ToShaderType(stage.Kind) != type) throw new ArgumentException("Stage kind mismatch: " + identity);
        var owner = GpuShaderContracts.Registry.Programs.Values.OrderBy(p => p.Identity, StringComparer.Ordinal).First(p => p.Stages.Any(s => s.Identity == identity));
        var selected = new ShaderLoadPlan(new ShaderSettings(owner)).Stages.Single(s => s.Stage.Identity == identity);
        var loaded = SpirvStageLoader.Load(selected,
            path => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path)));
        TestShaderInterfaces.TrackShader(loaded.Shader, loaded.Contract);
        return loaded.Shader;
    }
    #endregion
}
