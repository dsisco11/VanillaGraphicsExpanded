using System.Text;
using ShaderBuildTool.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Tests.Unit.Spirv.Cache;

/// <summary>Checks cache identifier encoding and canonical full-digest key rules.</summary>
public sealed class ShaderCacheKeyTests
{
    #region Public API
    /// <summary>The shared encoder matches the RFC 4648 unpadded Base32 test vectors.</summary>
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY")]
    [InlineData("fo", "MZXQ")]
    [InlineData("foo", "MZXW6")]
    [InlineData("foob", "MZXW6YQ")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI")]
    public void EncoderMatchesKnownVectors(string text, string expected)
    {
        Assert.Equal(expected, ShaderVariantIdentifier.Encode(Encoding.ASCII.GetBytes(text)));
    }

    /// <summary>Runtime identifiers preserve their established truncation and exact filename bytes.</summary>
    [Fact]
    public void RuntimeIdentifierRemainsUnchanged()
    {
        Assert.Equal("M3T4QK2JXMUR3UE4RYBAISBRDQ", ShaderVariantIdentifier.Create("fixture-key"));
        Assert.Equal(26, ShaderVariantIdentifier.Create("fixture-key").Length);
    }

    /// <summary>Cache references keep all SHA-256 bits with uppercase alphabet and canonical trailing bits.</summary>
    [Fact]
    public void CacheKeysAreCanonicalFullDigestBase32()
    {
        string key = ShaderCacheKey.Create("first", "second");
        Assert.Equal(52, key.Length);
        Assert.True(ShaderCacheKey.IsValid(key));
        Assert.Matches("^[A-Z2-7]{51}[AQ]$", key);
        Assert.NotEqual(key, ShaderCacheKey.Create("firstsecond"));
        Assert.True(ShaderCacheKey.IsValid(new string('A', 52)));
        Assert.True(ShaderCacheKey.IsValid(new string('A', 51) + "Q"));
        Assert.False(ShaderCacheKey.IsValid(new string('A', 51) + "B"));
        Assert.False(ShaderCacheKey.IsValid(key.ToLowerInvariant()));
        Assert.False(ShaderCacheKey.IsValid(key + "="));
        Assert.False(ShaderCacheKey.IsValid("../" + key));
        Assert.False(ShaderCacheKey.IsValid(ShaderBuildIdentities.Hash("legacy")));
        Assert.False(ShaderCacheKey.IsValid(null));
        Assert.Equal(ShaderVariantIdentifier.Encode(System.Security.Cryptography.SHA256.HashData([1, 2, 3])), ShaderCacheKey.Content([1, 2, 3]));
    }

    #endregion
}
