using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Checks the expanded near-field tracing shader contract without a graphics context.</summary>
public sealed class NearFieldUboTests
{
    #region Public API
    /// <summary>Large absolute coordinates become precise small relative origin bounds in the shader block.</summary>
    [Theory]
    [InlineData(16777216)]
    [InlineData(-16777216)]
    public void OriginDomainUsesRelativeCoordinates(int origin)
    {
        // Arrange
        using var ubo=new LumOnNearFieldParamsUbo();
        var bounds=new PartitionBounds(new(origin+.25,origin+1.5,origin-2.25),new(origin+16.25,origin+17.5,origin+13.75));
        // Act and Assert
        ubo.Set(new VectorInt3(origin,origin,origin),160,128,16,bounds,24);
        Assert.Equal(origin, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.MappingOffset));
        Assert.Equal(16, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.StepCellOffset + sizeof(int)));
        Assert.Equal(1, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.StepCellOffset + 2 * sizeof(int)));
        Assert.Equal(.25f, UboPacking.ReadFloat(ubo.Bytes, LumOnNearFieldParamsUbo.SupportedBoundsMinOffset));
        Assert.Equal(24, UboPacking.ReadFloat(ubo.Bytes, LumOnNearFieldParamsUbo.SupportedBoundsMinOffset + 3 * sizeof(float)));
        Assert.Equal(16.25f, UboPacking.ReadFloat(ubo.Bytes, LumOnNearFieldParamsUbo.SupportedBoundsMaxOffset));
        Assert.Equal(origin, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.NearFieldDomainOffset));
        Assert.Equal(1, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.NearFieldDomainOffset + 3 * sizeof(int)));
        Assert.Equal(origin + 160, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.NearFieldDomainMaxOffset));
        Assert.Equal(origin, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.SurfaceDomainOffset));
        ubo.Set(default,0);
        Assert.Equal(0, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.StepCellOffset + 2 * sizeof(int)));
        Assert.Equal(0, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.NearFieldDomainOffset + 3 * sizeof(int)));
        Assert.Equal(0, UboPacking.ReadInt32(ubo.Bytes, LumOnNearFieldParamsUbo.SurfaceDomainOffset + 3 * sizeof(int)));
    }
    #endregion
}
