using System.Numerics;
using Newtonsoft.Json;
using VanillaGraphicsExpanded.PBR.Materials;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR.Materials;

/// <summary>Verifies isolated material surface construction rules.</summary>
[Collection("PbrMaterialRegistry")]
public sealed class BRDFPropertiesTests
{
    #region Public API
    /// <summary>Dielectric and metallic materials keep the texture-derived diffuse and Fresnel split.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void SurfaceBuilderPreservesDiffuseAndFresnelConvention(float metallic)
    {
        // Arrange: compare both metallic endpoints with the same authored roughness and color.
        var properties = new BRDFProperties(0.2f, metallic, 0.5f, default, PbrOverrideScale.Identity);
        var color = new Vector3(0.8f, 0.4f, 0.2f);

        // Act: convert the authored values into a render-facing surface.
        var surface = PbrMaterialSurfaceBuilder.Build(properties, color);

        // Assert: preserve diffuse/Fresnel routing and the remaining material channels.
        Assert.Equal(metallic == 0 ? color : Vector3.Zero, surface.DiffuseAlbedo);
        Assert.Equal(metallic == 0 ? new Vector3(0.04f) : color, surface.SpecularF0);
        Assert.Equal(0.2f, surface.Roughness);
        Assert.Equal(metallic, surface.Metallic);
        Assert.Equal(0.5f, surface.Emissive);
    }

    /// <summary>Surface conversion applies scale multipliers, clamps sampled color, and clamps emissive output.</summary>
    [Fact]
    public void SurfaceBuilderPreservesScaleAndClamping()
    {
        // Arrange: suppress two authored channels and use a texture color outside the normalized range.
        var properties = new BRDFProperties(0.8f, 0.6f, 12, default, new(2, 0, 0, 1, 1));

        // Act: build with the channel-suppressing scale.
        var scaled = PbrMaterialSurfaceBuilder.Build(properties, new Vector3(-1, 0.5f, 2));

        // Assert: sampled color clamps and zero multipliers suppress metallic and emissive values.
        Assert.Equal(new PbrMaterialSurface(1, 0, 0, new Vector3(0, 0.5f, 1), new Vector3(0.04f)), scaled);

        // Act: restore identity scale while retaining the authored emissive value.
        var hdr = PbrMaterialSurfaceBuilder.Build(properties with { Scale = PbrOverrideScale.Identity }, Vector3.One);

        // Assert: diffuse color reflects the metallic split and emissive output clamps to one.
        Assert.Equal(1, hdr.Emissive);
        Assert.Equal(0.4f, hdr.DiffuseAlbedo.X, 6);
        Assert.Equal(0.4f, hdr.DiffuseAlbedo.Y, 6);
        Assert.Equal(0.4f, hdr.DiffuseAlbedo.Z, 6);
        Assert.InRange(hdr.SpecularF0.X, 0.61599f, 0.61601f);
    }
    #endregion
}
