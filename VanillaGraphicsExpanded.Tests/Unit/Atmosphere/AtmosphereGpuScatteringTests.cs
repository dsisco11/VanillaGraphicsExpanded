using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Compares the vectorized atmosphere source model with its retained scalar reference.</summary>
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereGpuScatteringTests
{
    #region Public API
    /// <summary>The default vectorized lookup preserves scalar transport around the ground-level horizon.</summary>
    [Fact]
    public void GroundHorizonVectorizedSourceMatchesScalarReference()
    {
        // Arrange
        float[] cosines = [-.002f, .002f, -1f / 31f, 1f / 31f];
        // Act
        var scalar = AtmosphereMultipleScattering.Build(1, useScalarReference: true);
        var vectorized = AtmosphereMultipleScattering.Build(1);
        // Assert
        foreach (float cosine in cosines)
        {
            Vector3 expected = scalar.Sample(.001f, cosine) * AtmosphereModel.SolarAmplitude;
            Vector3 actual = vectorized.Sample(.001f, cosine) * AtmosphereModel.SolarAmplitude;
            Assert.True(Vector3.Distance(expected, actual) <= 1e-4f + .01f * expected.Length(),
                $"horizon cosine={cosine:G9}, scalar={expected}, vectorized={actual}");
        }
    }
    #endregion
}
