using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Loads reusable test fixtures through the strict production binary loader.</summary>
internal static class BuiltShaderFixture
{
    /// <summary>Retains the numeric interface alongside the raw handle used by existing fixtures.</summary>
    public static int Load(string asset, ShaderType type)
    {
        var loaded = SpirvStageLoader.Load(asset, type, null,
            path => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", "shaders", path)));
        TestShaderInterfaces.TrackShader(loaded.Shader, loaded.Contract);
        return loaded.Shader;
    }
}
