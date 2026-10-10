using System.Text.Json.Nodes;
using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Qualifies receipt-first reuse independently of intermediate cache health.</summary>
public sealed class ShaderReceiptShortcutTests
{
    #region Public API
    /// <summary>A compatible unchanged receipt does not open or repair intermediate records or compiler artifacts.</summary>
    [Fact]
    public void UnchangedReceiptSkipsDamagedIntermediateCaches()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string cache = Path.Combine(fixture.Output, "_cache");
        var paths = Directory.GetFiles(cache, "*", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p) != "file-hashes.json").ToArray();
        Assert.NotEmpty(paths);
        foreach (string path in paths) File.WriteAllText(path, "invalid cached data");
        var prior = fixture.ContentSnapshot();
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        Assert.All(paths, path => Assert.Equal("invalid cached data", File.ReadAllText(path)));
        Assert.Equal(prior.ToArray(), fixture.ContentSnapshot().ToArray());
        // When an input changes the ordinary pipeline must validate and repair the now-needed cache.
        File.WriteAllText(Path.Combine(fixture.Shaders, "fixture.inc"), "#define FACTOR 0.7\n");
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        Assert.NotEqual(prior.ToArray(), fixture.ContentSnapshot().ToArray());
    }

    /// <summary>Earlier compatible receipts without the compact dependency snapshot refresh once.</summary>
    [Fact]
    public void MissingDependencyInventoryRefreshesReceipt()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string path = Path.Combine(fixture.Output, "build-receipt.json");
        var receipt = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        receipt.Remove("ConsumedInputs");
        receipt.Remove("BaseInputs");
        File.WriteAllText(path, receipt.ToJsonString());
        Assert.Equal(0, fixture.Build(2, clean: false, incremental: true));
        var updated = JsonNode.Parse(File.ReadAllText(path))!;
        Assert.NotNull(updated["ConsumedInputs"]);
        Assert.NotNull(updated["BaseInputs"]);
    }

    /// <summary>Removal of one stored dependency cannot turn a stale external input into receipt success.</summary>
    [Fact]
    public void TamperedDependencyInventoryIsNotAccepted()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, fixture.Build(2));
        string path = Path.Combine(fixture.Output, "build-receipt.json");
        var receipt = JsonNode.Parse(File.ReadAllText(path))!;
        receipt["ConsumedInputs"]!.AsArray().RemoveAt(0);
        File.WriteAllText(path, receipt.ToJsonString());
        var hashes = new ShaderFileHashIndex(fixture.Output, true);
        Assert.False(ShaderBuildReceipt.TryReuse(fixture.Output, receipt["BaseInputs"]!.GetValue<string>(),
            hashes, new(), out _));
    }
    #endregion
}
