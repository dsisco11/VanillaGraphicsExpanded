using System.Numerics;
using VanillaGraphicsExpanded.PBR.HeldLighting;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.Lighting;

/// <summary>Checks held-light coordinate and projection calculations with deterministic matrix inputs.</summary>
[Collection("Held lighting")]
public sealed class HeldLightTransformTests
{
    #region Public API
    /// <summary>Large world coordinates retain sub-block offsets after double-precision view translation.</summary>
    [Fact]
    public void ViewPositionPreservesSubBlockOffsetsAndNeighborEntries()
    {
        // Arrange: use large world coordinates and sentinel neighbors to expose precision loss or overrun.
        double[] view = Mat4d.Create();
        Mat4d.RotateY(view, view, Math.PI / 2);
        Mat4d.Translate(view, view, -30_000_000, -128, 30_000_000);
        float[] result = Enumerable.Repeat(-999f, 9).ToArray();

        // Act: write only the selected view-position entry after double-precision translation.
        HeldLightSources.WriteViewPosition(result, 1, view, new Vec3d(30_000_000.25, 129.5, -29_999_999.5));

        // Assert: neighboring entries and the fractional translated coordinates are preserved.
        Assert.Equal(-999, result[0]);
        Assert.Equal(-999, result[8]);
        Assert.Equal(0.5f, result[3], 5);
        Assert.Equal(1.5f, result[4], 5);
        Assert.Equal(-0.25f, result[5], 5);
    }

    /// <summary>Hand projection correction matches full engine matrix unprojection across projection settings.</summary>
    [Theory]
    [InlineData(50, 70)]
    [InlineData(90, 55)]
    [InlineData(70, 70)]
    public void FirstPersonProjectionMatchesParticlePath(float normalDegrees, float handDegrees)
    {
        // Arrange: create normal and hand projection matrices with the same view transform.
        float[] view = Mat4f.Create();
        Mat4f.RotateY(view, view, 0.45f);
        float[] normal = Mat4f.Create();
        float[] hand = Mat4f.Create();
        Mat4f.Perspective(normal, normalDegrees * GameMath.DEG2RAD, 1.7f, 0.1f, 1000f);
        Mat4f.Perspective(hand, handDegrees * GameMath.DEG2RAD, 1.7f, 0.1f, 1000f);
        var input = new Vec4f(0.4f, 1.1f, -0.8f, 1);
        Vec4f clip = new Matrixf(hand).Mul(view).TransformVector(input);
        Vec4f expected = new Matrixf(view).Invert().Mul(new Matrixf(normal).Invert()).TransformVector(clip);

        // Act: reproject the hand clip-space point through the held-light correction.
        Vector4 actual = HeldLightAttachment.ReprojectHand(new Vector4(input.X, input.Y, input.Z, input.W),
            view, normal, handDegrees * GameMath.DEG2RAD);

        // Assert: corrected coordinates match the engine matrix unprojection.
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }
    #endregion
}
