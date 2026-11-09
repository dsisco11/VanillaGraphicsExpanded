using System.Text.Json.Nodes;
using ShaderBuildTool.Spirv;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Unit.Spirv.Cache;

/// <summary>Checks malformed record references are rejected before creating storage.</summary>
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

    #endregion
}
