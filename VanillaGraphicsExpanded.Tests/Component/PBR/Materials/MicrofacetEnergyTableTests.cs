using System.Numerics;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.PBR.Materials;

/// <summary>Verifies packaged microfacet energy data, texture reduction, and physical response.</summary>
public sealed class MicrofacetEnergyTableTests
{
    #region Public API
    /// <summary>Normal-view energy agrees with an independent projected-hemisphere integration.</summary>
    [Theory]
    [InlineData(.25f)]
    [InlineData(.5f)]
    [InlineData(.75f)]
    [InlineData(1f)]
    public void NormalEnergyMatchesIndependentQuadrature(float roughness)
    {
        // Arrange: obtain the separate numerical reference and the packaged lookup.
        var expected = MicrofacetEnergyReference.NormalEnergy(roughness);

        // Act: sample the table at normal view.
        var actual = MicrofacetEnergyTable.Shared.Sample(roughness, 1);

        // Assert: both coefficients remain within the independent integration tolerances.
        Assert.InRange(actual.X, expected.E * .9993, expected.E * 1.0007);
        Assert.InRange(Math.Abs(actual.Y - expected.Ef), 0, .000003);
    }

    /// <summary>Reflectance endpoints conserve white-furnace energy and preserve the Schlick grazing term.</summary>
    [Theory]
    [InlineData(.04f, 0f)]
    [InlineData(.04f, .001f)]
    [InlineData(.5f, .5f)]
    [InlineData(1f, 1f)]
    public void ReflectanceEndpointsRemainPhysical(float roughness, float viewCosine)
    {
        // Arrange: sample the energy coefficients at each selected material/view point.
        Vector2 energy = MicrofacetEnergyTable.Shared.Sample(roughness, viewCosine);

        // Act: evaluate white, black, and ordered colored reflectance.
        Vector3 white = MicrofacetEnergyTable.Reflectance(energy, Vector3.One);
        Vector3 black = MicrofacetEnergyTable.Reflectance(energy, Vector3.Zero);
        Vector3 colored = MicrofacetEnergyTable.Reflectance(energy, new(.04f, .5f, 1));

        // Assert: energy and channel ordering obey their physical endpoint contracts.
        Assert.InRange(white.X, .999999f, 1);
        Assert.Equal(new Vector3(energy.Y), black);
        Assert.True(colored.X <= colored.Y && colored.Y <= colored.Z);
        Assert.InRange(colored.X, 0, 1);
    }

    /// <summary>Diffuse cache coefficients reserve the projected-view average independently of texture filtering.</summary>
    [Theory]
    [InlineData(.04f)]
    [InlineData(.5f)]
    [InlineData(1f)]
    public void CacheDiffuseUsesProjectedViewAverage(float roughness)
    {
        // Arrange: prepare the material and an independent uniform cosine-grid integral.
        var table = MicrofacetEnergyTable.Shared;
        Vector3 albedo = new(.5f, .25f, .125f), f0 = new(.04f, .5f, 1f);
        var material = new PbrMaterialSurface(roughness, 0, 0, albedo, f0);
        const int intervals = 4096;
        Vector3 sum = Vector3.Zero;

        // Act: compare the table's diffuse cache adapter with Simpson integration over the hemisphere.
        for (int index = 0; index <= intervals; index++)
        {
            float mu = (float)index / intervals;
            float weight = index is 0 or intervals ? 1 : index % 2 == 0 ? 2 : 4;
            sum += MicrofacetEnergyTable.Reflectance(table.Sample(roughness, mu), f0) * (2 * mu * weight);
        }
        Vector3 actual = table.DiffuseAlbedo(material);
        Vector3 expected = albedo * (Vector3.One - sum / (3 * intervals));

        // Assert: the independently integrated diffuse value agrees and cannot become negative.
        for (int channel = 0; channel < 3; channel++)
            Assert.InRange(actual[channel], expected[channel] - .0001f, expected[channel] + .0001f);
        Assert.InRange(actual.Z, 0, .000001f);
    }

    /// <summary>Texture generation half-rounds admitted nodes without mutating the physical source table.</summary>
    [Theory]
    [InlineData(16)] [InlineData(32)] [InlineData(64)] [InlineData(128)]
    public void TexturePayloadIsHalfRoundedWithoutChangingPhysicalSource(int size)
    {
        // Arrange: retain the physical CPU table before producing one supported texture tier.
        var table = MicrofacetEnergyTable.Shared;
        var before = table.Samples.ToArray();

        // Act: build reduced texture coefficients for the selected resolution.
        var pixels = table.CreateTextureData(size);

        // Assert: each coefficient is representable and bounded, the source is unchanged, and unsupported sizes fail.
        Assert.Equal(size * size * 2, pixels.Length);
        for (int i = 0; i < pixels.Length; i += 2)
        {
            Assert.InRange(pixels[i], .25f, 1);
            Assert.InRange(pixels[i + 1], 0, pixels[i]);
            Assert.Equal(pixels[i], (float)(Half)pixels[i]);
            Assert.Equal(pixels[i + 1], (float)(Half)pixels[i + 1]);
        }
        Assert.Equal(before, table.Samples.ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => table.CreateTextureData(96));
    }

    /// <summary>Increasing texture tiers converge toward the physical source under the existing error budgets.</summary>
    [Fact]
    public void TextureTiersConvergeTowardPhysicalSource()
    {
        // Arrange: track maximum coefficient errors while retaining the established off-node sampling grid.
        double previousE = double.PositiveInfinity, previousEf = double.PositiveInfinity;

        // Act and assert: every supported tier reduces error and stays within its qualified error budget.
        foreach (int size in new[] { 16, 32, 64, 128 })
        {
            double maximumE = 0, maximumEf = 0;
            for (int y = 0; y < 193; y++)
            for (int x = 0; x < 193; x++)
            {
                float u = (x + .37f) / 193, v = (y + .61f) / 193;
                float roughness = .04f + .96f * u * u, view = v * v * v * v;
                var physical = MicrofacetEnergyTable.Shared.Sample(roughness, view);
                var reduced = MicrofacetTextureReference.Sample(size, roughness, view).Value;
                maximumE = Math.Max(maximumE, Math.Abs(reduced.X / physical.X - 1));
                maximumEf = Math.Max(maximumEf, Math.Abs(reduced.Y - physical.Y));
            }
            TestContext.Current.TestOutputHelper!.WriteLine($"tier={size} maximumRelativeE={maximumE:R} maximumAbsoluteEf={maximumEf:R}");
            Assert.True(maximumE <= previousE && maximumEf <= previousEf, "Increasing the texture resolution must reduce worst-case source error.");
            var budget = size switch { 16 => (.03, .02), 32 => (.01, .006), 64 => (.003, .002), _ => (.005, .0005) };
            Assert.InRange(maximumE, 0, budget.Item1);
            Assert.InRange(maximumEf, 0, budget.Item2);
            previousE = maximumE;
            previousEf = maximumEf;
        }
    }
    #endregion
}
