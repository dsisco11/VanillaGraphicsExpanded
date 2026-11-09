using VanillaGraphicsExpanded.PBR.Materials.Energy;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies domain clamping and finite-coordinate admission for microfacet sampling.</summary>
public sealed class MicrofacetSamplingRulesTests
{
    #region Public API
    /// <summary>Finite out-of-domain values clamp to endpoint samples while nonfinite coordinates are rejected.</summary>
    [Fact]
    public void SamplingAdmitsOnlyFiniteCoordinates()
    {
        // Arrange: use the shared parsed table as the unit boundary.
        var table = MicrofacetEnergyTable.Shared;

        // Act: sample finite values outside both domains.
        var low = table.Sample(-1, -1);
        var high = table.Sample(5, 5);

        // Assert: finite inputs clamp and nonfinite inputs fail before lookup.
        Assert.Equal(table.Sample(.04f, 0), low);
        Assert.Equal(table.Sample(1, 1), high);
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Sample(float.NaN, .5f));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.Sample(.5f, float.PositiveInfinity));
        Assert.Equal(128 * 128 * 2, table.Samples.Length);
    }
    #endregion
}
