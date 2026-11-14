using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Checks semantic shadow-object selectors are validated before parameter publication changes.</summary>
public sealed class ShadowObjectParameterValidationTests
{
    #region Public API
    /// <summary>Accepts supported warp suppression selectors and rejects invalid ones without replacing the captured state.</summary>
    [Fact]
    public void SetFlagsValidatesWarpSuppressionBeforeMutation()
    {
        // Arrange: exercise all supported native selectors through the owning parameter API.
        using var parameters = new ShadowObjectParameters();
        parameters.SetFlags(17, 0);
        parameters.SetFlags(17, 1);
        parameters.SetFlags(17, 2);
        var retained = parameters.Capture();

        // Act/Assert: malformed selectors are rejected before either the contents or captured revision changes.
        Assert.Throws<ArgumentOutOfRangeException>(() => parameters.SetFlags(41, -1));
        Assert.Same(retained, parameters.Capture());
        Assert.Throws<ArgumentOutOfRangeException>(() => parameters.SetFlags(43, 3));
        Assert.Same(retained, parameters.Capture());
    }
    #endregion
}
