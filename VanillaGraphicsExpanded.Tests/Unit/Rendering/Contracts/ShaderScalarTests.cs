using System.Globalization;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Exact normalization, source spelling and 32-bit specialization argument regressions.</summary>
public sealed class ShaderScalarTests
{
    #region Public API
    #region Scalar representations
    /// <summary>Encodes signed, unsigned, float and Boolean constants without numeric widening.</summary>
    [Fact]
    public void ScalarEncodingAndGlslSpellingAreCentralized()
    {
        // Arrange: Select representative signed, unsigned, floating-point, and Boolean values.
        // Act: Encode the scalar values and request their canonical source spellings.
        var signed = ShaderScalar.From(-1);
        // Assert: The bit patterns, GLSL types, literals, and signed-zero normalization are stable.
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
    public void BooleanSpellingsAgree(string text, bool value)
    {
        // Arrange: xUnit supplies one accepted Boolean spelling and its canonical value.
        // Act: Parse the spelling into the Boolean scalar representation.
        var actual = ShaderScalar.Parse(ShaderScalarType.Bool, text);
        // Assert: The parsed value matches the independently supplied canonical Boolean.
        Assert.Equal(ShaderScalar.From(value), actual);
    }

    /// <summary>Integral parsing preserves full 32-bit domains and supports equivalent integral spellings.</summary>
    [Fact]
    public void IntegralParsingPreservesBoundaries()
    {
        // Arrange: Parse the signed/unsigned extrema and equivalent integral spellings.
        // Act: Normalize all four input strings.
        var signedMinimum = ShaderScalar.Parse(ShaderScalarType.Int, "-2147483648");
        var unsignedMaximum = ShaderScalar.Parse(ShaderScalarType.UInt, "4294967295");
        var exponent = ShaderScalar.Parse(ShaderScalarType.Int, "2e1");
        var decimalValue = ShaderScalar.Parse(ShaderScalarType.Int, "20.00");
        // Assert: Full 32-bit boundaries survive and equivalent integral strings normalize identically.
        Assert.Equal(ShaderScalar.From(int.MinValue), signedMinimum);
        Assert.Equal(ShaderScalar.From(uint.MaxValue), unsignedMaximum);
        Assert.Equal(ShaderScalar.From(20), exponent);
        Assert.Equal(ShaderScalar.From(20), decimalValue);
    }

    /// <summary>Rejects fractional integers, overflow, nonfinite floats and unsupported CLR scalar types.</summary>
    [Fact]
    public void InvalidValuesAreNotCoerced()
    {
        // Arrange: Exercise fractional/overflowing integer inputs, nonfinite floats, and unsupported CLR types.
        // Act: Parse or encode each invalid scalar through the public contract.
        // Assert: Invalid values retain their specific rejection behavior instead of being coerced.
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
        // Arrange: Declare a finite enum domain with a noncontiguous underlying value.
        var mode = new ShaderOption<Mode>("MODE", Mode.Off, [Mode.Off, Mode.On]);
        // Act: Parse an included and an excluded enum value.
        // Assert: The scalar representation uses the enum's underlying integer value.
        Assert.Equal("4", mode.Parse("4").Canonical);
        Assert.Throws<ArgumentException>(() => mode.Parse("2"));
        Assert.Equal(ShaderScalar.From(4), ShaderScalar.From(Mode.On));
    }

    /// <summary>Formatting/parsing is independent of decimal separators in the caller's culture.</summary>
    [Fact]
    public void ScalarTextIsCultureInvariant()
    {
        // Arrange: Save the caller's current culture for unconditional restoration.
        var prior = CultureInfo.CurrentCulture;
        try
        {
            // Act: Parse invariant decimal text while a comma-decimal culture is active.
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            // Assert: A period remains the decimal separator and a comma is rejected.
            Assert.Equal("1.5", ShaderScalar.Parse(ShaderScalarType.Float, "1.5").Canonical);
            Assert.Throws<FormatException>(() => ShaderScalar.Parse(ShaderScalarType.Float, "1,5"));
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Representative finite integer values used to test enum scalar conversion.</summary>
    private enum Mode { Off = 0, On = 4 }

    /// <summary>Unsupported 64-bit enum storage used to verify the 32-bit scalar boundary.</summary>
    private enum WideMode : long { Off }
    #endregion
}
