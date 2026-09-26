using ShaderBuildTool.Generation;

namespace ShaderBuildTool.Tests;

/// <summary>Checks automatic angular-table publication and its content-based incremental build contract.</summary>
public sealed class LumonOctahedralShGenerationTests
{
    #region Generation and incremental repair
    /// <summary>The migrated generator preserves every coefficient of the checked-in angular table.</summary>
    [Fact]
    public void GeneratedCoefficientsMatchCheckedInTable()
    {
        using var fixture = new ShaderBuildFixture();
        LumonOctahedralShWeights.Generate(fixture.Shaders);
        string expected = Path.Combine(fixture.Repository, "VanillaGraphicsExpanded", "assets", "vanillagraphicsexpanded", "shaders", "includes", "lumon_octahedral_sh9_weights.glsl");
        string generated = Path.Combine(fixture.Shaders, "includes", "lumon_octahedral_sh9_weights.glsl");
        // Only the first provenance line changes when moving the generator into the build tool.
        Assert.Equal(File.ReadAllLines(expected).Skip(1), File.ReadAllLines(generated).Skip(1));
    }

    /// <summary>Every invocation repairs generated input before checking receipts without recompiling identical restored content.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncrementalBuildRepairsGeneratedInputBeforeCheckingReceipt(bool remove)
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(1));
        string include = Path.Combine(fixture.Shaders, "includes", "lumon_octahedral_sh9_weights.glsl");
        string original = File.ReadAllText(include);
        var outputs = Directory.EnumerateFiles(fixture.Output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.GetLastWriteTimeUtc);
        if (remove) File.Delete(include);
        else File.WriteAllText(include, "corrupt generated input");
        Assert.Equal(0, fixture.Build(1, incremental: true));
        Assert.Equal(original, File.ReadAllText(include));
        Assert.All(outputs, pair => Assert.Equal(pair.Value, File.GetLastWriteTimeUtc(pair.Key)));
        // A subsequent unchanged invocation still regenerates the include, while compiled outputs stay current.
        File.SetLastWriteTimeUtc(include, DateTime.UnixEpoch);
        Assert.Equal(0, fixture.Build(1, incremental: true));
        Assert.True(File.GetLastWriteTimeUtc(include) > DateTime.UnixEpoch);
        Assert.All(outputs, pair => Assert.Equal(pair.Value, File.GetLastWriteTimeUtc(pair.Key)));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(fixture.Shaders)!, ".lumon-sh-*.tmp"));
    }

    /// <summary>Help and invalid arguments cannot create generated shader input.</summary>
    [Fact]
    public void NonBuildInvocationsDoNotGenerateInput()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, Program.Main(["--help", "--assetsRoot", fixture.Assets, "--outputRoot", fixture.Output]));
        Assert.Equal(2, Program.Main(["--assetsRoot", fixture.Assets]));
        Assert.False(Directory.Exists(Path.Combine(fixture.Shaders, "includes")));
    }
    #endregion
}
