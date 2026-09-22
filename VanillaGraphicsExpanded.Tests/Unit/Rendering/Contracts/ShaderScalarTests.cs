using System.Globalization;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Exact normalization, source spelling and 32-bit specialization argument regressions.</summary>
public sealed class ShaderScalarTests
{
    private enum Mode { Off = 0, On = 4 }
    private enum WideMode : long { Off }

    #region Scalar representations
    /// <summary>Encodes signed, unsigned, float and Boolean constants without numeric widening.</summary>
    [Fact]
    public void ScalarEncodingAndGlslSpellingAreCentralized()
    {
        var signed = ShaderScalar.From(-1);
        Assert.Equal(uint.MaxValue, signed.Bits);
        Assert.Equal("int", signed.GlslType);
        Assert.Equal("-1", signed.GlslLiteral);
        var unsigned = ShaderScalar.From(uint.MaxValue);
        Assert.Equal("4294967295u", unsigned.GlslLiteral);
        Assert.Equal("uint", unsigned.GlslType);
        Assert.Equal(0x3fc00000u, ShaderScalar.From(1.5f).Bits);
        Assert.Equal("float", ShaderScalar.From(1f).GlslType);
        Assert.Equal("1.0", ShaderScalar.From(1f).GlslLiteral);
        Assert.Equal("bool", ShaderScalar.From(true).GlslType);
        Assert.Equal("true", ShaderScalar.From(true).GlslLiteral);
        Assert.Equal(1u, ShaderScalar.From(true).Bits);
        Assert.Equal("0", ShaderScalar.From(false).Canonical);
        Assert.Equal(ShaderScalar.From(0f), ShaderScalar.From(-0f));
    }

    /// <summary>Compatibility spellings normalize to canonical Boolean values.</summary>
    [Theory]
    [InlineData("1.0", true)] [InlineData("true", true)] [InlineData("0e2", false)] [InlineData("FALSE", false)]
    public void BooleanSpellingsAgree(string text, bool value) =>
        Assert.Equal(ShaderScalar.From(value), ShaderScalar.Parse(ShaderScalarType.Bool, text));

    /// <summary>Integral parsing preserves full 32-bit domains and supports equivalent integral spellings.</summary>
    [Fact]
    public void IntegralParsingPreservesBoundaries()
    {
        Assert.Equal(ShaderScalar.From(int.MinValue), ShaderScalar.Parse(ShaderScalarType.Int, "-2147483648"));
        Assert.Equal(ShaderScalar.From(uint.MaxValue), ShaderScalar.Parse(ShaderScalarType.UInt, "4294967295"));
        Assert.Equal(ShaderScalar.From(20), ShaderScalar.Parse(ShaderScalarType.Int, "2e1"));
        Assert.Equal(ShaderScalar.From(20), ShaderScalar.Parse(ShaderScalarType.Int, "20.00"));
    }

    /// <summary>Rejects fractional integers, overflow, nonfinite floats and unsupported CLR scalar types.</summary>
    [Fact]
    public void InvalidValuesAreNotCoerced()
    {
        Assert.Throws<OverflowException>(() => ShaderScalar.Parse(ShaderScalarType.Int, "1.1"));
        Assert.Throws<OverflowException>(() => ShaderScalar.Parse(ShaderScalarType.Int, "1.000000000000000000000000000001"));
        Assert.Throws<OverflowException>(() => ShaderScalar.Parse(ShaderScalarType.Int, "1e-100"));
        Assert.Throws<OverflowException>(() => ShaderScalar.Parse(ShaderScalarType.Int, "2147483648"));
        Assert.Throws<OverflowException>(() => ShaderScalar.Parse(ShaderScalarType.UInt, "-1"));
        Assert.ThrowsAny<ArgumentException>(() => ShaderScalar.Parse(ShaderScalarType.Bool, "2"));
        foreach (string text in new[] { "NaN", "Infinity", "-Infinity", "1e100" })
            Assert.ThrowsAny<ArgumentException>(() => ShaderScalar.Parse(ShaderScalarType.Float, text));
        Assert.Throws<ArgumentException>(() => ShaderScalar.From(double.Epsilon));
        Assert.Throws<ArgumentException>(() => ShaderScalar.From(WideMode.Off));
    }

    /// <summary>Finite enum domains use underlying integer values in source and binary keys.</summary>
    [Fact]
    public void EnumOptionsUseExplicitDomains()
    {
        var mode = new ShaderOption<Mode>("MODE", Mode.Off, [Mode.Off, Mode.On]);
        Assert.Equal("4", mode.Parse("4").Canonical);
        Assert.Throws<ArgumentException>(() => mode.Parse("2"));
        Assert.Equal(ShaderScalar.From(4), ShaderScalar.From(Mode.On));
    }

    /// <summary>Formatting/parsing is independent of decimal separators in the caller's culture.</summary>
    [Fact]
    public void ScalarTextIsCultureInvariant()
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal("1.5", ShaderScalar.Parse(ShaderScalarType.Float, "1.5").Canonical);
            Assert.Throws<FormatException>(() => ShaderScalar.Parse(ShaderScalarType.Float, "1,5"));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
    #endregion
}
