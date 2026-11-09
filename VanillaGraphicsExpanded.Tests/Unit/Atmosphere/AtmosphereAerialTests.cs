using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Constrains finite atmospheric transport independently of texture publication.</summary>
public sealed class AtmosphereAerialTests
{
    #region Public API
    /// <summary>Tensor lanes and partial batches reproduce the independent scalar finite-path integration.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    [InlineData(128)]
    public void TensorFinitePathsMatchScalar(int count)
    {
        // Arrange: allocate transport outputs and derive a representative ray from the theory boundary.
        Vector3[] directions = new Vector3[count];
        for (int i = 0; i < count; i++) directions[i] = Vector3.Normalize(new Vector3(1, (i % 7 - 3) * .25f, i % 3));
        Vector4[] batched = new Vector4[count * AtmosphereAerialPerspective.Depth], trans = new Vector4[batched.Length];
        Vector4[] scalar = new Vector4[AtmosphereAerialPerspective.Depth], scalarTrans = new Vector4[scalar.Length];
        // Act and Assert: compare each batched ray's radiance and transmission with its scalar reference.
        AtmosphereModel.AerialBatch(directions, Vector3.UnitY, 2, 1, null, batched, trans);
        for (int i = 0; i < count; i++)
        {
            AtmosphereModel.AerialRay(directions[i], Vector3.UnitY, 2, 1, null, scalar, scalarTrans);
            for (int z = 0; z < scalar.Length; z++)
            {
                Assert.InRange(Vector4.Distance(scalar[z], batched[z * count + i]), 0, 1e-5f + 2e-6f * scalar[z].Length());
                Assert.InRange(Vector4.Distance(scalarTrans[z], trans[z * count + i]), 0, 1e-5f);
            }
        }
    }

    /// <summary>Distance endpoints preserve identity and cumulative transport remains physical for every direction.</summary>
    [Theory]
    [InlineData(.001f, 1f)]
    [InlineData(2f, 0f)]
    [InlineData(25f, -1f)]
    [InlineData(99f, .1f)]
    public void FinitePathsPreserveIdentityAndMonotonicTransmission(float altitude, float up)
    {
        // Arrange output grids and the normalized ray whose vertical component comes from this theory row.
        Vector4[] radiance = new Vector4[AtmosphereAerialPerspective.Depth];
        Vector4[] transmission = new Vector4[radiance.Length];
        Vector3 direction = Vector3.Normalize(new(MathF.Sqrt(1 - up * up), up, 0));
        // Act by integrating the finite path; assert its zero-distance identity and monotonic channel values.
        AtmosphereModel.AerialRay(direction, Vector3.UnitY, altitude, 1, null, radiance, transmission);
        Assert.Equal(new Vector4(0, 0, 0, 1), radiance[0]);
        Assert.Equal(Vector4.One, transmission[0]);
        for (int slice = 1; slice < radiance.Length; slice++)
        for (int channel = 0; channel < 3; channel++)
        {
            Assert.True(float.IsFinite(radiance[slice][channel]));
            Assert.InRange(radiance[slice][channel], radiance[slice - 1][channel], float.MaxValue);
            Assert.InRange(transmission[slice][channel], 0, transmission[slice - 1][channel]);
        }
    }

    /// <summary>The phase functions distinguish sunward scattering from an equally long perpendicular path.</summary>
    [Fact]
    public void RadianceDependsOnViewDirectionRelativeToSun()
    {
        // Arrange separate output grids for equal-length sunward and perpendicular viewing directions.
        Vector4[] toward = new Vector4[AtmosphereAerialPerspective.Depth], across = new Vector4[toward.Length];
        Vector4[] transmission = new Vector4[toward.Length];
        // Act: integrate equal-length rays toward and across the sun.
        AtmosphereModel.AerialRay(Vector3.UnitX, Vector3.UnitX, 1, 1, null, toward, transmission);
        AtmosphereModel.AerialRay(Vector3.UnitZ, Vector3.UnitX, 1, 1, null, across, transmission);
        // Assert: sunward scattering produces greater terminal radiance.
        Assert.True(toward[^1].X > across[^1].X);
    }

    /// <summary>A higher observer looking upward traverses less dense atmospheric material.</summary>
    [Fact]
    public void AltitudeChangesIntegratedExtinction()
    {
        // Arrange shared radiance storage and separate transmission outputs for two observer altitudes.
        Vector4[] radiance = new Vector4[AtmosphereAerialPerspective.Depth], low = new Vector4[radiance.Length], high = new Vector4[radiance.Length];
        // Act: integrate the same upward ray from low and high observer altitudes.
        AtmosphereModel.AerialRay(Vector3.UnitY, Vector3.UnitY, .001f, 1, null, radiance, low);
        AtmosphereModel.AerialRay(Vector3.UnitY, Vector3.UnitY, 25, 1, null, radiance, high);
        // Assert: the higher observer retains more light in every channel.
        Assert.True(high[^1].X > low[^1].X);
        Assert.True(high[^1].Y > low[^1].Y);
        Assert.True(high[^1].Z > low[^1].Z);
    }

    /// <summary>The logarithmic grid spans the complete finite ray and resolves nearby surfaces more densely.</summary>
    [Theory]
    [InlineData(.001f)]
    [InlineData(100f)]
    [InlineData(1000f)]
    public void DistanceGridHasExactIdentityAndBoundedEndpoint(float boundary)
    {
        // Arrange: use each finite ray boundary from the theory rows.
        // Act and Assert: sample every grid slice and verify identity, monotonic spacing, and the bounded endpoint.
        Assert.Equal(0, AtmosphereAerialPerspective.Distance(0, boundary));
        float previous = 0, previousStep = 0;
        for (int slice = 1; slice < AtmosphereAerialPerspective.Depth; slice++)
        {
            float distance = AtmosphereAerialPerspective.Distance(slice, boundary);
            Assert.True(distance >= previous);
            if (distance < boundary) Assert.True(distance - previous >= previousStep);
            previousStep = distance - previous; previous = distance;
        }
        Assert.InRange(MathF.Abs(previous - boundary), 0, boundary * 2e-6f);
    }
    #endregion
}
