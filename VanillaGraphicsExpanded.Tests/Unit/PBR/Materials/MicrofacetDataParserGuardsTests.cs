using VanillaGraphicsExpanded.PBR.Materials.Energy;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies that malformed embedded microfacet data is rejected before a table is exposed.</summary>
public sealed class MicrofacetDataParserGuardsTests
{
    #region Public API
    /// <summary>A truncated payload cannot provide a partial lookup table.</summary>
    [Fact]
    public void TruncatedAssetIsRejected()
    {
        // Arrange: provide fewer bytes than the table header requires.
        using var input = new MemoryStream(new byte[4]);

        // Act and assert: parsing fails before publishing any table state.
        Assert.Throws<InvalidDataException>(() => new MicrofacetEnergyTable(input));
    }

    /// <summary>Nonfinite or out-of-range coefficients invalidate the complete payload.</summary>
    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(.24f, 0f)]
    [InlineData(1.01f, 0f)]
    [InlineData(.5f, -.001f)]
    [InlineData(.5f, .51f)]
    public void CorruptedCoefficientIsRejected(float e, float ef)
    {
        // Arrange: copy the real payload, then corrupt the first coefficient pair.
        using var embedded = typeof(MicrofacetEnergyTable).Assembly.GetManifestResourceStream("VGE.MicrofacetEnergy")!;
        using var copy = new MemoryStream();
        embedded.CopyTo(copy);
        byte[] bytes = copy.ToArray();
        BitConverter.GetBytes(e).CopyTo(bytes, 32);
        BitConverter.GetBytes(ef).CopyTo(bytes, 36);
        using var input = new MemoryStream(bytes);

        // Act and assert: parser validation rejects the malformed coefficient pair.
        Assert.Throws<InvalidDataException>(() => new MicrofacetEnergyTable(input));
    }
    #endregion
}
