using System;
using VanillaGraphicsExpanded.PBR.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Shadows;

/// <summary>Checks immediate invalidation of disposed surface shadow inputs.</summary>
public sealed class SurfaceShadowInputSetTests
{
    #region Public API
    /// <summary>Rejects parameter access after the owning set is disposed, including after a policy update.</summary>
    [Fact]
    public void DisposedSetCannotBeCapturedAfterPolicyUpdate()
    {
        // Arrange: create a shadow input set and apply a policy update before disposal.
        using var inputs = new SurfaceShadowInputSet(null, null, null, new ShadowPageSettings { DepthBiasTexels = .25 });
        inputs.UpdatePolicy(new ShadowPageSettings { DepthBiasTexels = 1.75 });

        // Act: dispose the set, then attempt to capture its parameters.
        inputs.Dispose();

        // Assert: disposed inputs reject further parameter access.
        Assert.Throws<ObjectDisposedException>(() => _ = inputs.Parameters);
    }
    #endregion
}
