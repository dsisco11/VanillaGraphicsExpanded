using System.Text.Json.Nodes;
using ShaderBuildTool.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Exercises atomic private envelopes against malformed references, corruption and aliases.</summary>
public sealed class ShaderRecordStoreTests
{
    #region Public API
    /// <summary>Digest-only references cannot escape the owned record family.</summary>
    [Theory]
    [InlineData("../outside")]
    [InlineData("C:/outside")]
    [InlineData("")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void InvalidReferencesAreLocalMisses(string key)
    {
        using var fixture = new ShaderBuildFixture();
        var store = new ShaderRecordStore(fixture.Output, "tests");
        var reasons = new List<string>();
        Assert.False(store.TryRead<string>(key, out _, reasons.Add));
        Assert.Single(reasons);
        Assert.Throws<ArgumentException>(() => store.Store(key, "payload"));
        Assert.False(Directory.Exists(fixture.Output));
    }

    /// <summary>Partial, incompatible and tampered envelope data always returns a reported local miss.</summary>
    [Theory]
    [InlineData("partial")]
    [InlineData("version")]
    [InlineData("identity")]
    [InlineData("digest")]
    [InlineData("payload")]
    [InlineData("null")]
    public void TamperedEnvelopeIsRejected(string corruption)
    {
        using var fixture = new ShaderBuildFixture();
        var store = new ShaderRecordStore(fixture.Output, "tests");
        string key = ShaderCacheKey.Create("record");
        store.Store(key, "payload");
        string path = Path.Combine(fixture.Output, "_cache", "tests", key + ".json");
        var envelope = JsonNode.Parse(File.ReadAllText(path))!;
        switch (corruption)
        {
            case "version": envelope["Version"] = 100; break;
            case "identity": envelope["Key"] = ShaderCacheKey.Create("different"); break;
            case "digest": envelope["Digest"] = ShaderBuildIdentities.Hash("different"); break;
            case "payload": envelope["Payload"] = Convert.ToBase64String([1, 2, 3]); break;
        }
        File.WriteAllText(path, corruption == "partial" ? "{" : corruption == "null" ? "null" : envelope.ToJsonString());
        var reasons = new List<string>();
        Assert.False(store.TryRead<string>(key, out _, reasons.Add));
        Assert.Single(reasons);
    }

    /// <summary>Concurrent equivalent aliases leave a complete reusable record and no partial files.</summary>
    [Fact]
    public async Task ConcurrentAliasesPublishCompleteEnvelopes()
    {
        using var fixture = new ShaderBuildFixture();
        var store = new ShaderRecordStore(fixture.Output, "tests");
        string key = ShaderCacheKey.Create("record");
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            store.Store(key, "payload");
            Assert.True(store.TryRead<string>(key, out var restored));
            Assert.Equal("payload", restored);
        }, TestContext.Current.CancellationToken)));
        Assert.Single(Directory.GetFiles(Path.Combine(fixture.Output, "_cache", "tests")));
        Assert.True(new ShaderRecordStore(fixture.Output, "tests").TryRead<string>(key, out var value));
        Assert.Equal("payload", value);
    }
    #endregion
}
