using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Tests.Support.Atmosphere;

namespace VanillaGraphicsExpanded.Tests.Unit.Atmosphere;

/// <summary>Checks multiple-scattering transport bounds and cancellation behavior.</summary>
public sealed class AtmosphereMultipleScatteringTests
{
    #region Public API
    /// <summary>A passive atmosphere and fully reflective ground cannot amplify an isotropic unit field.</summary>
    [Fact(Skip = "CPU multiple-scattering numerical validation is disabled.")]
    public void TransferFeedbackConservesEnergy()
    {
        // Arrange the aerosol, altitude and direction matrix for a fully reflective passive-medium bound.
        foreach (float aerosol in new[] { .1f, 1f, 8f })
        foreach (float altitude in new[] { .001f, 2f, 25f, 99f })
        foreach (Vector3 direction in AtmosphereTestDirections.Create(32))
        {
            // Act and Assert: evaluate transfer for each ray and constrain reflected energy to a passive medium.
            var transfer = AtmosphereModel.MultipleScatteringTransfer(Vector3.Normalize(direction), Vector3.UnitY, altitude, aerosol, 1, 24);
            for (int channel = 0; channel < 3; channel++)
                Assert.InRange(transfer.Feedback[channel], 0, 1.00001f);
        }
    }

    /// <summary>Cancellation prevents spending work on a discarded world or weather request.</summary>
    [Fact]
    public void BuildHonorsCancellation()
    {
        // Arrange an already-canceled token to prove table construction is skipped.
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        // Act and Assert: reject the canceled request before table construction begins.
        Assert.Throws<OperationCanceledException>(() => AtmosphereMultipleScattering.Build(1, cancellation.Token));
    }
    #endregion
}
