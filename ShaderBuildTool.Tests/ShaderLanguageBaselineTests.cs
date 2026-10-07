using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Tests;

/// <summary>Checks that real compilation enforces the authored source language baseline.</summary>
public sealed class ShaderLanguageBaselineTests
{
    #region Public API
    /// <summary>Storage buffers need an explicit newer language version rather than an implicit compiler upgrade.</summary>
    [Theory]
    [InlineData(330, false)]
    [InlineData(430, true)]
    public async Task StorageBufferRequiresDeclaredLanguageSupport(int version, bool succeeds)
    {
        using var fixture = new ShaderBuildFixture();
        var stage = new ShaderStageContract("baseline.vsh", "baseline.vsh", ShaderStageKind.Vertex, new());
        var selected = new ShaderStageSelection(stage, new Dictionary<string, ShaderScalar>());
        string source = $"#version {version} core\nlayout(std430,binding=0) buffer Data {{ vec4 value; }}; void main() {{ gl_Position=value; }}";
        string emitted = new ShaderVariantSource(fixture.Assets, "vanillagraphicsexpanded").Emit(source, selected);
        string input = Path.Combine(fixture.Root, "baseline.glsl");
        string output = Path.Combine(fixture.Root, "baseline.spv");
        File.WriteAllText(input, emitted);
        var result = await ShaderCompilerProcess.CompileAsync(fixture.Repository, input, output, "vertex",
            "opengl4.5", false, "main", TestContext.Current.CancellationToken);
        Assert.Equal(succeeds, result.ExitCode == 0);
        if (succeeds)
            Assert.True(File.Exists(output), result.StandardError + result.StandardOutput);
        else
            Assert.Contains("not supported", result.StandardError + result.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }
    #endregion
}
