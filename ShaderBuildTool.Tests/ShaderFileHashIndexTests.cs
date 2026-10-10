using System.Security.Cryptography;
using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Checks persisted metadata reuse and explicit content verification for shader build inputs.</summary>
public sealed class ShaderFileHashIndexTests
{
    #region Metadata reuse
    /// <summary>Separate invocations reuse unchanged inputs without rewriting their single shared index.</summary>
    [Fact]
    public void RestartReusesHashesAndPreservesUnchangedIndex()
    {
        using var fixture = new ShaderBuildFixture();
        string[] inputs = Directory.GetFiles(fixture.Shaders);
        var initial = new ShaderFileHashIndex(fixture.Output);
        foreach (string path in inputs)
            Assert.Equal(SHA256.HashData(File.ReadAllBytes(path)), initial.GetHash(path));
        initial.Save();
        Assert.Equal(inputs.Length, initial.HashedFiles);
        string indexPath = Path.Combine(fixture.Output, "_cache", "file-hashes.json");
        DateTime marker = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(indexPath, marker);
        var restarted = new ShaderFileHashIndex(fixture.Output);
        foreach (string path in inputs) restarted.GetHash(path);
        restarted.Save();
        Assert.Equal(inputs.Length, restarted.ReusedFiles);
        Assert.Equal(0, restarted.HashedFiles);
        Assert.Equal(marker, File.GetLastWriteTimeUtc(indexPath));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*.json", SearchOption.AllDirectories));
    }

    /// <summary>Either changed size or changed write time invalidates a stored content hash.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ChangedMetadataRehashesInput(bool changeSize)
    {
        using var fixture = new ShaderBuildFixture();
        string path = Path.Combine(fixture.Shaders, "fixture.inc");
        var initial = new ShaderFileHashIndex(fixture.Output);
        byte[] previous = initial.GetHash(path);
        initial.Save();
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        string source = File.ReadAllText(path);
        File.WriteAllText(path, changeSize ? source + "\n" : source.Replace("0.5", "0.7"));
        File.SetLastWriteTimeUtc(path, changeSize ? timestamp : timestamp.AddSeconds(10));
        var restarted = new ShaderFileHashIndex(fixture.Output);
        byte[] current = restarted.GetHash(path);
        Assert.False(previous.SequenceEqual(current));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(path)), current);
        Assert.Equal(1, restarted.HashedFiles);
        Assert.Equal(0, restarted.ReusedFiles);
    }
    #endregion

    #region Verification and persistence
    /// <summary>Strict shared-input memoization expires at commit verification even when file metadata stays unchanged.</summary>
    [Fact]
    public void VerificationEpochRehashesPreservedMetadataEdits()
    {
        using var fixture = new ShaderBuildFixture();
        string path = Path.Combine(fixture.Shaders, "fixture.inc");
        var index = new ShaderFileHashIndex(fixture.Output, verifyContents: true);
        byte[] before = index.GetHash(path);
        Assert.Equal(before, index.GetHash(path));
        Assert.Equal(1, index.HashedFiles);
        DateTime write = File.GetLastWriteTimeUtc(path), creation = File.GetCreationTimeUtc(path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("0.5", "0.7"));
        File.SetLastWriteTimeUtc(path, write);
        File.SetCreationTimeUtc(path, creation);
        index.BeginVerification();
        byte[] after = index.GetHash(path);
        Assert.False(before.SequenceEqual(after));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(path)), after);
        Assert.Equal(2, index.HashedFiles);
        Assert.Equal(after, index.GetHash(path));
        Assert.Equal(2, index.HashedFiles);
    }

    /// <summary>Strict verification detects content edits even when size and timestamps are preserved.</summary>
    [Fact]
    public void VerificationDetectsPreservedMetadataEdit()
    {
        using var fixture = new ShaderBuildFixture();
        string path = Path.Combine(fixture.Shaders, "fixture.inc");
        var initial = new ShaderFileHashIndex(fixture.Output);
        byte[] previous = initial.GetHash(path);
        initial.Save();
        DateTime timestamp = File.GetLastWriteTimeUtc(path);
        File.WriteAllText(path, File.ReadAllText(path).Replace("0.5", "0.7"));
        File.SetLastWriteTimeUtc(path, timestamp);
        var verified = new ShaderFileHashIndex(fixture.Output, verifyContents: true);
        byte[] current = verified.GetHash(path);
        Assert.False(previous.SequenceEqual(current));
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(path)), current);
        Assert.Equal(1, verified.HashedFiles);
        Assert.Equal(0, verified.ReusedFiles);
    }

    /// <summary>Unreadable index content falls back to computing input hashes.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    public void MalformedIndexFallsBackToHashing(string contents)
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(Path.Combine(fixture.Output, "_cache"));
        File.WriteAllText(Path.Combine(fixture.Output, "_cache", "file-hashes.json"), contents);
        string path = Path.Combine(fixture.Shaders, "fixture.inc");
        var index = new ShaderFileHashIndex(fixture.Output);
        Assert.Equal(SHA256.HashData(File.ReadAllBytes(path)), index.GetHash(path));
        Assert.Equal(1, index.HashedFiles);
        index.Save();
        var restarted = new ShaderFileHashIndex(fixture.Output);
        restarted.GetHash(path);
        Assert.Equal(1, restarted.ReusedFiles);
    }

    /// <summary>Saving a new invocation removes input entries that were not visited.</summary>
    [Fact]
    public void SavePrunesRemovedInputs()
    {
        using var fixture = new ShaderBuildFixture();
        string retained = Path.Combine(fixture.Shaders, "fixture.inc");
        string removed = Path.Combine(fixture.Shaders, "fixture.csh");
        var initial = new ShaderFileHashIndex(fixture.Output);
        initial.GetHash(retained);
        initial.GetHash(removed);
        initial.Save();
        var next = new ShaderFileHashIndex(fixture.Output);
        next.GetHash(retained);
        next.Save();
        var restarted = new ShaderFileHashIndex(fixture.Output);
        restarted.GetHash(retained);
        restarted.GetHash(removed);
        Assert.Equal(1, restarted.ReusedFiles);
        Assert.Equal(1, restarted.HashedFiles);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Output, "_cache"), "*.tmp", SearchOption.AllDirectories));
    }
    #endregion
}
