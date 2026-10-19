using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using CloudVolumeStorage = VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks selected volume accounting, stable material keys and finite half representation.</summary>
public sealed class CloudVolumeDomainTests
{
    #region Public API
    /// <summary>Full coverage, guards and metadata fit the selected exact payload without losing negative keys.</summary>
    [Fact]
    public void LayoutAccountsForGuardsAndRetainsMaterialIdentity()
    {
        var d = new CloudVolumeDomain(200, default, -100, 100);
        Assert.Equal(515, d.Size); Assert.Equal(17504850, d.Voxels); Assert.Equal(71825, d.Blocks);
        Assert.Equal(391679376, d.Bytes); Assert.Equal(280077600, d.CopyBytes);
        Assert.True(2 * d.Bytes < CloudVolumeDomain.PeakLimit);
        var p = new CloudPlacement(-53, 87, 0, 0, -7, 5, .2, .7, new Vector3(3), 0);
        var a = new CloudVolumeDomain(5, p, -1, 1);
        var b = new CloudVolumeDomain(5, p with {CenterX = p.CenterX + 1, WindTileX = p.WindTileX + 1}, -1, 1, a);
        Assert.Equal(a, b); Assert.True(a.CanScroll(b));
        Assert.Equal(d.Bottom, new CloudVolumeDomain(200, default, -20, 20, d).Bottom);
        var expanded = new CloudVolumeDomain(200, default, -1000, 1000, d);
        Assert.True(expanded.Band > d.Band); Assert.False(d.CanScroll(expanded));
        Assert.Equal(expanded.Band, new CloudVolumeDomain(200, default, 0, 0, expanded).Band);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeDomain(201, default, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeDomain(200, default, -20000, 20000));
        Assert.Throws<NotSupportedException>(() => CloudVolumeStorage.Validate(d, new GraphicsCapabilities()));
        Assert.Equal(4, CloudVolumeDomain.Mod(-1, 5));
    }

    /// <summary>Downward half storage never exceeds the declared source, including subnormal and large values.</summary>
    [Fact]
    public void EncodingBoundsRepeatedHalfPublication()
    {
        foreach (float cap in new[] {0f, .000001f, 1f, 1e8f, float.MaxValue})
        {
            var e = new CloudVolumeEncoding(new Vector3(cap)); Assert.True(e.IsValid);
            var physical = new Vector3(cap * .12345f);
            Vector3 encoded = e.Encode(physical);
            Assert.True(encoded.X * (double)e.Scale <= physical.X);
            Assert.Equal(encoded.X, (float)(Half)encoded.X);
            Assert.True(e.Encode(encoded * e.Scale).X <= encoded.X);
        }
        Assert.False(default(CloudVolumeEncoding).IsValid);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeEncoding(new Vector3(float.NaN)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CloudVolumeEncoding(new Vector3(-1)));
    }
    #endregion
}
