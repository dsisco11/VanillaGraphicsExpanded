using VanillaGraphicsExpanded.PBR.Liquids;

namespace VanillaGraphicsExpanded.Tests.Unit.PBR;

/// <summary>Qualifies camera admission for the actual signed water accumulator arithmetic.</summary>
public sealed class WaterBoundaryStorageTests
{
    #region Public API
    /// <summary>Ordinary and off-axis perspective extents fit while depth-floor enlargement and extreme cones are rejected.</summary>
    [Fact]
    public void PerspectiveEnvelopeIncludesDepthFloorAndOffAxisCone()
    {
        float[] projection = Perspective(.1f, 1000);
        Assert.True(WaterBoundaryStorage.CameraAdmitted(projection, .1f, 1000));
        projection[8] = .5f; projection[9] = -.25f;
        Assert.True(WaterBoundaryStorage.CameraAdmitted(projection, .1f, 1000));
        Assert.True(WaterBoundaryStorage.CameraAdmitted(Perspective(.1f, 10000), .1f, 10000));
        // Nominal far-plane length alone fits; the real reconstruction denominator floor does not.
        Assert.False(WaterBoundaryStorage.CameraAdmitted(Perspective(.1f, 100000), .1f, 100000));
        projection[0] = float.Epsilon;
        Assert.False(WaterBoundaryStorage.CameraAdmitted(projection, .1f, 1000));
    }

    /// <summary>Unsupported projection or clip metadata is rejected before any boundary draw.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void InvalidCameraMetadataIsRejected(int kind)
    {
        float[] projection = Perspective(.1f, 1000);
        float near = .1f, far = 1000;
        switch (kind)
        {
            case 0: near = .008f; break;
            case 1: far = near; break;
            case 2: near = float.NaN; break;
            case 3: far = float.PositiveInfinity; break;
            case 4: projection[11] = 0; break;
            case 5: projection[0] = 0; break;
            case 6: projection[8] = float.NaN; break;
            case 7: projection[14] = -.001f; break;
        }
        Assert.False(WaterBoundaryStorage.CameraAdmitted(projection, near, far));
    }
    #endregion

    #region Private
    /// <summary>Constructs the OpenGL perspective coefficients independently of the admission helper.</summary>
    private static float[] Perspective(float near, float far) =>
        [1,0,0,0, 0,1,0,0, 0,0,-(far+near)/(far-near),-1, 0,0,-2*far*near/(far-near),0];
    #endregion
}
