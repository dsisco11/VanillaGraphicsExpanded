using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Qualifies compiler content reuse across differing source and working paths.</summary>
public sealed class ShaderCompilerPathIdentityTests
{
    #region Public API
    /// <summary>Optimized binaries without debug metadata are identical across input and working directories.</summary>
    [Fact]
    public async Task ReleaseCompilerResultsAreIndependentOfSourceAndWorkingPaths()
    {
#if !DEBUG
        using var fixture = new ShaderBuildFixture();
        const string source = "#version 450 core\nlayout(local_size_x=1) in; layout(std430,binding=0) buffer Data{uint value;}; void main(){value=1;}";
        var binaries = new List<byte[]>();
        // Both working directories inherit the same repository tool manifest while source paths differ.
        foreach (string directory in new[] { "ShaderBuildTool", "ShaderBuildCatalog" })
        {
            string input = Path.Combine(fixture.Root, directory + ".glsl");
            string output = Path.Combine(fixture.Root, directory + ".spv");
            await File.WriteAllTextAsync(input, source, TestContext.Current.CancellationToken);
            var result = await ShaderCompilerProcess.CompileAsync(Path.Combine(fixture.Repository, directory),
                input, output, "compute", "opengl4.5", false, "main", TestContext.Current.CancellationToken);
            Assert.True(result.ExitCode == 0, result.StandardError);
            binaries.Add(await File.ReadAllBytesAsync(output, TestContext.Current.CancellationToken));
        }
        Assert.NotEmpty(binaries[0]);
        Assert.Equal(binaries[0], binaries[1]);
#else
        // Debug binaries retain source provenance, so their cache keys deliberately include both paths.
        var cache = new ShaderVariantCache("unused", "compiler");
        Assert.NotEqual(cache.Key("source", "compute", "main", "a.glsl", "one"),
            cache.Key("source", "compute", "main", "b.glsl", "two"));
        await Task.CompletedTask;
#endif
    }
    #endregion
}
