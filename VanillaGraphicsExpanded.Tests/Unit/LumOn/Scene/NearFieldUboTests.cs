using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks the expanded near-field tracing shader contract without a graphics context.</summary>
public sealed class NearFieldUboTests
{
    #region Packed coordinates
    /// <summary>Large absolute coordinates become precise small relative origin bounds in the shader block.</summary>
    [Theory]
    [InlineData(16777216)]
    [InlineData(-16777216)]
    public void OriginDomainUsesRelativeCoordinates(int origin)
    {
        using var ubo=new LumOnNearFieldParamsUbo();
        var bounds=new PartitionBounds(new(origin+.25,origin+1.5,origin-2.25),new(origin+16.25,origin+17.5,origin+13.75));
        ubo.Set(new VectorInt3(origin,origin,origin),160,128,16,bounds,24);
        Assert.Equal(128,ubo.Bytes.Length);
        Assert.Equal(origin,BitConverter.ToInt32(ubo.Bytes[..4]));
        Assert.Equal(16,BitConverter.ToInt32(ubo.Bytes.Slice(20,4)));
        Assert.Equal(1,BitConverter.ToInt32(ubo.Bytes.Slice(24,4)));
        Assert.Equal(.25f,BitConverter.ToSingle(ubo.Bytes.Slice(32,4)));
        Assert.Equal(24,BitConverter.ToSingle(ubo.Bytes.Slice(44,4)));
        Assert.Equal(16.25f,BitConverter.ToSingle(ubo.Bytes.Slice(48,4)));
        Assert.Equal(origin,BitConverter.ToInt32(ubo.Bytes.Slice(64,4)));
        Assert.Equal(1,BitConverter.ToInt32(ubo.Bytes.Slice(76,4)));
        Assert.Equal(origin + 160,BitConverter.ToInt32(ubo.Bytes.Slice(80,4)));
        Assert.Equal(origin,BitConverter.ToInt32(ubo.Bytes.Slice(96,4)));
        ubo.Set(default,0);
        Assert.Equal(0,BitConverter.ToInt32(ubo.Bytes.Slice(24,4)));
        Assert.Equal(0,BitConverter.ToInt32(ubo.Bytes.Slice(76,4)));
        Assert.Equal(0,BitConverter.ToInt32(ubo.Bytes.Slice(108,4)));
    }
    #endregion
}
