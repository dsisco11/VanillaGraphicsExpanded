namespace ShaderBuildTool.Tests;

/// <summary>Checks real TinyAst emission, shaderc outputs and entry-point publication with multiple workers.</summary>
public sealed class ShaderParallelBuildTests
{
    #region Compilation equivalence and failure
    /// <summary>Serial and parallel jobs produce byte-identical inputs and binaries at the same stable paths.</summary>
    [Fact]
    public void SerialAndParallelArtifactsMatchAndIncrementalBuildIsUnchanged()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(1));
        var serial = fixture.ContentSnapshot();
        Assert.Equal(6, serial.Count);
        Assert.Equal(0, fixture.Build(4));
        Assert.Equal(serial.ToArray(), fixture.ContentSnapshot().ToArray());
        var times = Directory.EnumerateFiles(fixture.Output, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.GetLastWriteTimeUtc);
        Assert.Equal(0, fixture.Build(1, incremental: true));
        Assert.All(times, pair => Assert.Equal(pair.Value, File.GetLastWriteTimeUtc(pair.Key)));
    }

    /// <summary>A failed non-clean rebuild removes prior success and incomplete compiler outputs, then recovers.</summary>
    [Fact]
    public void FailedRebuildCannotReuseSuccessReceiptOrPublishPartialBinary()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(4));
        string path = Path.Combine(fixture.Shaders, "fixture.csh");
        string valid = File.ReadAllText(path);
        File.WriteAllText(path, valid + "\ninvalid compiler input");
        Assert.Equal(1, fixture.Build(4, clean: false, incremental: true));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "build-receipt.json")));
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Output, "_tmp"), "*.spv", SearchOption.AllDirectories));
        Assert.False(File.Exists(Path.Combine(fixture.Output, "vanillagraphicsexpanded", "shaders", "fixture.csh.spv")));
        File.WriteAllText(path, valid);
        Assert.Equal(0, fixture.Build(4, incremental: true));
        Assert.Equal(6, fixture.ContentSnapshot().Count);
    }

    /// <summary>A second entry point cannot delete an output whose owner still holds its lease.</summary>
    [Fact]
    public void CompetingBuildCannotCleanOwnedOutput()
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(fixture.Output);
        string sentinel = Path.Combine(fixture.Output, "sentinel");
        File.WriteAllText(sentinel, "owned");
        using var lease = ShaderBuildTool.Spirv.ShaderOutputLease.Acquire(fixture.Output);
        Assert.Equal(1, fixture.Build(4));
        Assert.Equal("owned", File.ReadAllText(sentinel));
    }
    #endregion
}
