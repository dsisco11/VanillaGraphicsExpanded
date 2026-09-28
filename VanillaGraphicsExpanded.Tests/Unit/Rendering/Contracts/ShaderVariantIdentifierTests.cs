using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks portable variant identifiers against independently encoded SHA-256 prefixes.</summary>
public sealed class ShaderVariantIdentifierTests
{
    #region Identifier encoding
    /// <summary>Known vectors cover UTF-8 input, fixed width, and zero padding of the final Base32 group.</summary>
    [Theory]
    [InlineData("", "4OYMIQUY7QOBJGX36TEJS35ZEQ")]
    [InlineData("abc", "XJ4BNP4PAHH6UQKBIDPF3LRCEM")]
    [InlineData("A=2;Z=0", "FHCCJEIBPHVL346SQN2VRBWQBA")]
    [InlineData("SHADER=☀", "MEM3KLBPEFYKB5XPZGMJUXMWMM")]
    public void IdentifierMatchesIndependentBase32Vector(string key, string expected)
    {
        string actual = ShaderVariantIdentifier.Create(key);
        Assert.Equal(expected, actual);
        Assert.Equal(actual, ShaderVariantIdentifier.Create(key));
        Assert.Matches("^[A-Z2-7]{26}$", actual);
    }
    #endregion
}
