using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Checks source identity is atomically paired with the existing retained light payload.</summary>
public sealed class VgeLightsSourcePublicationTests
{
    #region Public API
    /// <summary>Movement updates payload while preserving the owner key; replacement changes only that key.</summary>
    [Fact]
    public void NativeCaptureRetainsIdentityAlongsideCurrentPayload()
    {
        // Arrange: two native owners share positions initially, but have distinct persistent keys.
        using var buffer = new VgeLightsUniformBuffer();
        var first = new ShadowSourceKey(10, 0, ShadowSourceKind.Entity, 21, 31, 0);
        var second = new ShadowSourceKey(10, 0, ShadowSourceKind.Hand, 22, 32, 1);

        // Act: move the first source and replace the second owner's emission lifetime.
        buffer.Capture(2, [0, 0, 0, 4, 5, 6], [1, 2, 3, 4, 5, 6], [first, second]);
        var moved = buffer.Lights;
        var replacement = second with { Lifetime = 33 };
        buffer.Capture(2, [8, 9, 10, 4, 5, 6], [1, 2, 3, 4, 5, 6], [first, replacement]);

        // Assert: current positions follow the current native payload and owner lifetimes never come from list indices.
        Assert.Equal(first, moved[0].Source);
        Assert.Equal(new System.Numerics.Vector3(0, 0, 0), moved[0].ViewPosition);
        Assert.Equal(new System.Numerics.Vector3(8, 9, 10), buffer.Lights[0].ViewPosition);
        Assert.Equal(first, buffer.Lights[0].Source);
        Assert.Equal(replacement, buffer.Lights[1].Source);
        Assert.Equal(new System.Numerics.Vector4(1, 2, 3, 0), buffer.Lights[0].Payload);
    }

    /// <summary>Unknown source identities preserve illumination payload but cannot qualify cached shadow ownership.</summary>
    [Fact]
    public void MissingIdentityRetainsLightAndReportsUnknownShadowOwner()
    {
        // Arrange: capture valid illumination without an identity provider.
        using var buffer = new VgeLightsUniformBuffer();
        buffer.Capture(1, [2, 3, 4], [8, 7, 6]);

        // Act: inspect only shadow metadata while leaving packed lighting available.
        var light = buffer.Lights[0];

        // Assert: missing collection integration cannot be mistaken for a shadowable source or removed light.
        Assert.False(light.Source.IsKnown);
        Assert.Equal(new System.Numerics.Vector3(2, 3, 4), light.ViewPosition);
        Assert.Equal(new System.Numerics.Vector4(8, 7, 6, 0), light.Payload);
    }
    #endregion
}
