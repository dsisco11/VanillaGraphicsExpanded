using VanillaGraphicsExpanded.PBR.Liquids;

namespace VanillaGraphicsExpanded.Tests.Unit.Water;

/// <summary>Verifies camera admission against the signed water accumulator limits.</summary>
public sealed class WaterBoundaryStorageTests
{
    #region Public API
    /// <summary>Projection admission includes off-axis cone size and the depth reconstruction floor.</summary>
    [Fact]
    public void PerspectiveEnvelopeIncludesDepthFloorAndOffAxisCone()
    {
        // Arrange: prepare a perspective camera with ordinary near/far clip values.
        float[] projection = Perspective(.1f, 1000);

        // Act: evaluate ordinary, off-axis, and long-range perspective camera envelopes.
        bool ordinaryAdmitted = WaterBoundaryStorage.CameraAdmitted(projection, .1f, 1000);
        projection[8] = .5f;
        projection[9] = -.25f;
        bool offAxisAdmitted = WaterBoundaryStorage.CameraAdmitted(projection, .1f, 1000);
        bool longRangeAdmitted = WaterBoundaryStorage.CameraAdmitted(Perspective(.1f, 10000), .1f, 10000);

        // Assert: supported view cones fit within accumulator capacity.
        Assert.True(ordinaryAdmitted);
        Assert.True(offAxisAdmitted);
        Assert.True(longRangeAdmitted);

        // Act: test the reconstruction depth floor and an extreme off-axis cone.
        bool depthFloorAdmitted = WaterBoundaryStorage.CameraAdmitted(Perspective(.1f, 100000), .1f, 100000);
        projection[0] = float.Epsilon;
        bool extremeConeAdmitted = WaterBoundaryStorage.CameraAdmitted(projection, .1f, 1000);

        // Assert: unsafe reconstructed extents and extreme cones are rejected.
        Assert.False(depthFloorAdmitted);
        Assert.False(extremeConeAdmitted);
    }
    /// <summary>Unsupported projection coefficients and clip metadata are rejected.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void InvalidCameraMetadataIsRejected(int kind)
    {
        // Arrange: start with valid perspective coefficients and choose one invalid metadata condition.
        float[] projection = Perspective(.1f, 1000);
        float near = .1f;
        float far = 1000;
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

        // Act and assert: each unsupported projection or clip combination fails admission.
        Assert.False(WaterBoundaryStorage.CameraAdmitted(projection, near, far));
    }
    #endregion

    #region Private
    /// <summary>Constructs OpenGL perspective coefficients independently of the admission helper.</summary>
    private static float[] Perspective(float near, float far) =>
        [1,0,0,0, 0,1,0,0, 0,0,-(far+near)/(far-near),-1, 0,0,-2*far*near/(far-near),0];
    #endregion
}

