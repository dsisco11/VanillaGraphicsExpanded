using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using CloudVolumeStorage = VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.Unit.Clouds;

/// <summary>Checks selected volume accounting, stable material keys and finite half representation.</summary>
public sealed class CloudVolumeDomainTests
{
    #region Public API
    /// <summary>Full coverage, guards and metadata fit the selected exact payload without losing negative keys.</summary>
    [Fact]
    public void LayoutAccountsForGuardsAndRetainsMaterialIdentity()
    {
        // Arrange: choose a 200-unit request and vertical bounds that exercise guard accounting.
        const int size = 200;
        const int bottom = -100;
        const int top = 100;
        // Act: construct the initial volume domain.
        var d = new CloudVolumeDomain(size, default, bottom, top);
        // Assert: size, voxel count, buffer layout, and peak allocation match the contract.
        Assert.Equal(515, d.Size); Assert.Equal(17504850, d.Voxels); Assert.Equal(71825, d.Blocks);
        Assert.Equal(394825104, d.Bytes); Assert.Equal(280077600, d.CopyBytes);
        Assert.True(2 * d.Bytes < CloudVolumeDomain.PeakLimit);

        // Arrange: provide a placement with negative material tile keys.
        var p = new CloudPlacement(-53, 87, 0, 0, -7, 5, .2, .7, new Vector3(3), 0);
        // Act: construct a scroll-compatible pair and a taller vertical domain.
        var a = new CloudVolumeDomain(5, p, -1, 1);
        var b = new CloudVolumeDomain(5, p with {CenterX = p.CenterX + 1, WindTileX = p.WindTileX + 1}, -1, 1, a);
        var expanded = new CloudVolumeDomain(200, default, -1000, 1000, d);
        // Assert: material identity, scroll compatibility, vertical extent, and modulo wrapping hold.
        Assert.Equal(a, b); Assert.True(a.CanScroll(b));
        Assert.Equal(d.Bottom, new CloudVolumeDomain(200, default, -20, 20, d).Bottom);
        Assert.True(expanded.Band > d.Band); Assert.False(d.CanScroll(expanded));
        Assert.Equal(expanded.Band, new CloudVolumeDomain(200, default, 0, 0, expanded).Band);
        // Act/Assert: unsupported dimensions, vertical ranges, and storage capabilities are rejected.
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeDomain(201, default, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeDomain(200, default, -20000, 20000));
        Assert.Throws<NotSupportedException>(() => CloudVolumeStorage.Validate(d, new GraphicsCapabilities()));
        Assert.Equal(4, CloudVolumeDomain.Mod(-1, 5));
    }

    /// <summary>Downward half storage never exceeds the declared source, including subnormal and large values.</summary>
    [Fact]
    public void EncodingBoundsRepeatedHalfPublication()
    {
        // Arrange: cover zero, subnormal, ordinary, large and maximum finite encoding caps.
        foreach (float cap in new[] {0f, .000001f, 1f, 1e8f, float.MaxValue})
        {
            // Act: encode a fraction of each declared cap and publish the encoded value again.
            var e = new CloudVolumeEncoding(new Vector3(cap)); Assert.True(e.IsValid);
            var physical = new Vector3(cap * .12345f);
            Vector3 encoded = e.Encode(physical);
            // Assert: half conversion and repeated publication never exceed the source values.
            Assert.True(encoded.X * (double)e.Scale <= physical.X);
            Assert.Equal(encoded.X, (float)(Half)encoded.X);
            Assert.True(e.Encode(encoded * e.Scale).X <= encoded.X);
        }
        // Act/Assert: invalid default, NaN and negative caps are rejected.
        Assert.False(default(CloudVolumeEncoding).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeEncoding(new Vector3(float.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeEncoding(new Vector3(-1)));
    }
    #endregion
}
