using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Verifies light quadrature changes the source term without changing feedback.</summary>
public sealed class AtmosphereTransportRulesTests
{
    #region Public API
    /// <summary>Solar optical-depth refinement affects source radiance without altering isotropic feedback.</summary>
    [Fact]
    public void LightQuadratureRefinesSourceOnly()
    {
        // Arrange one view/sun pair and identical transport settings with coarse versus refined light quadrature.
        Vector3 direction = Vector3.Normalize(new Vector3(1, .1f, 0));
        Vector3 sun = Vector3.Normalize(new Vector3(1, .03f, 0));
        // Act by evaluating both quadrature budgets for the same transport ray.
        var coarse = AtmosphereModel.MultipleScatteringTransfer(direction, sun, 2, 1, .1f, 48, 2);
        var fine = AtmosphereModel.MultipleScatteringTransfer(direction, sun, 2, 1, .1f, 48, 96);
        // Assert feedback is unchanged while refined source radiance remains finite and positive.
        Assert.Equal(coarse.Feedback, fine.Feedback);
        Assert.True(Vector3.Distance(coarse.Source, fine.Source) > 1e-6f);
        for (int channel = 0; channel < 3; channel++)
            Assert.True(float.IsFinite(fine.Source[channel]) && fine.Source[channel] > 0);
    }
    #endregion
}
