using System.Globalization;
using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Celestial;

/// <summary>Checks built celestial shader artifacts against their authoritative runtime constants.</summary>
[Trait("Category", "GpuIntegration")]
[Collection("GPU")]
public sealed class SolarShaderArtifactTests
{
    #region Public API
    /// <summary>The shader half-angle remains identical to the authoritative atmosphere emitter.</summary>
    [Fact]
    public void SolarAngularRadiusMatchesTheAtmosphereEmitter()
    {
        // Arrange: Read the built shader artifact alongside the authoritative atmosphere constant.
        string source = TestAssetSnapshot.ReadBuiltText(Path.Combine(AppContext.BaseDirectory, "assets/shaders/includes/pbr_finite_disk_brdf.glsl"));
        // Act: Extract the shader's declared angular radius using invariant decimal syntax.
        var match = Regex.Match(source, @"VGE_SOLAR_ANGULAR_RADIUS\s*=\s*([0-9.]+)");
        // Assert: The artifact declares the same value consumed by the runtime emitter.
        Assert.True(match.Success);
        Assert.Equal(AtmosphereSolarDisk.AngularRadius, float.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture));
    }
    #endregion
}
