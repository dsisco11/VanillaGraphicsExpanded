using System;
using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.Lights;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Shadows;

/// <summary>Checks conservative receiver bounds for the two native perspective views.</summary>
public sealed class NativeShadowSurfaceReceiverBoundsTests
{
    private static readonly double[] Identity =
        [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];
    private static readonly double[] QuarterTurnView =
        [0, 0, -1, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 1];

    #region Public API
    /// <summary>Both ordinary and first-person views enclose independently calculated near/far corners at large origins.</summary>
    [Fact]
    public void EncloseIncludesEyeAndNearAndFarCornersAtLargeWorldOrigin()
    {
        var lights = Lights(originX: 1_000_000_000, originY: -2_000_000_000, originZ: 800_000_000,
            remainder: new(11.25f, -7.5f, 3.125f));
        const double aspect = 16.0 / 9.0, near = 0.1, far = 512;
        const double mainFov = Math.PI / 3, handsFov = Math.PI / 2;
        double[] mainProjection = Perspective(mainFov, aspect, near, far);
        double[] handsProjection = Perspective(handsFov, aspect, near, far);

        var main = NativeShadowSurfaceReceivers.Enclose(mainProjection, QuarterTurnView, lights);
        var hands = NativeShadowSurfaceReceivers.Enclose(handsProjection, QuarterTurnView, lights);

        double originX = lights.OriginX * 32.0 + lights.OriginRemainder.X;
        double originY = lights.OriginY * 32.0 + lights.OriginRemainder.Y;
        double originZ = lights.OriginZ * 32.0 + lights.OriginRemainder.Z;
        Assert.True(main.MinX < originX && main.MaxX > originX + far);
        Assert.True(main.MinY < originY && main.MaxY > originY);
        double horizontalFar = far * Math.Tan(mainFov / 2) * aspect;
        Assert.True(main.MinZ < originZ - horizontalFar && main.MaxZ > originZ + horizontalFar);
        AssertPerspectiveCorners(main, lights, mainFov, aspect, near, far);
        AssertPerspectiveCorners(hands, lights, handsFov, aspect, near, far);
        Assert.True(hands.MinX < main.MinX && hands.MaxX > main.MaxX);
        Assert.True(hands.MinY < main.MinY && hands.MaxY > main.MaxY);
        Assert.True(hands.MinZ < main.MinZ && hands.MaxZ > main.MaxZ);
        Assert.All(new[] { main.MinX, main.MinY, main.MinZ, main.MaxX, main.MaxY, main.MaxZ,
            hands.MinX, hands.MinY, hands.MinZ, hands.MaxX, hands.MaxY, hands.MaxZ },
            value => Assert.True(double.IsFinite(value)));
    }

    /// <summary>Singular and infinite-far projections cannot publish a finite receiver certificate.</summary>
    [Fact]
    public void EncloseRejectsSingularAndUnboundedPerspectiveMatrices()
    {
        var lights = Lights(0, 0, 0, Vector3.Zero);
        double[] singular = new double[16];
        double[] infiniteFar = Perspective(verticalFovRadians: Math.PI / 3, aspect: 1, near: 0.1, far: double.PositiveInfinity);

        Assert.Throws<InvalidOperationException>(() => NativeShadowSurfaceReceivers.Enclose(singular, Identity, lights));
        Assert.Throws<InvalidOperationException>(() => NativeShadowSurfaceReceivers.Enclose(infiniteFar, Identity, lights));
    }

    /// <summary>Liquid's positive clip-W offset admits a thin region beyond the ordinary far plane.</summary>
    [Fact]
    public void EncloseIncludesLiquidPositiveClipWAllowanceAndRejectsInvalidAllowance()
    {
        var lights = Lights(0, 0, 0, Vector3.Zero);
        const double near = .1, far = 1;
        var projection = Perspective(Math.PI / 3, 1, near, far);

        var ordinary = NativeShadowSurfaceReceivers.Enclose(projection, Identity, lights);
        var liquid = NativeShadowSurfaceReceivers.Enclose(projection, Identity, lights, positiveClipWAllowance: .008);
        // At this point the actual shader's unclamped clip Z is just beyond ordinary clip W,
        // while its .0008 / max(.1, clipZ) addition makes the point pass the far-plane test.
        const double viewZ = -1.002;
        double clipZ = projection[10] * viewZ + projection[14];
        double ordinaryClipW = -viewZ;
        double liquidClipW = ordinaryClipW + .0008 / Math.Max(.1, clipZ);
        var admittedPoint = new ShadowBounds(0, 0, viewZ, 0, 0, viewZ);

        Assert.True(clipZ > ordinaryClipW);
        Assert.True(clipZ <= liquidClipW);
        Assert.False(ordinary.Contains(admittedPoint));
        Assert.True(liquid.Contains(admittedPoint));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NativeShadowSurfaceReceivers.Enclose(projection, Identity, lights, positiveClipWAllowance: -.001));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NativeShadowSurfaceReceivers.Enclose(projection, Identity, lights, positiveClipWAllowance: double.NaN));
    }
    #endregion

    #region Private
    /// <summary>Builds the native column-major OpenGL perspective matrix used by both camera projections.</summary>
    private static double[] Perspective(double verticalFovRadians, double aspect, double near, double far)
    {
        double scale = 1 / Math.Tan(verticalFovRadians / 2);
        double depth = double.IsPositiveInfinity(far) ? -1 : -(far + near) / (far - near);
        double offset = double.IsPositiveInfinity(far) ? -2 * near : -(2 * far * near) / (far - near);
        return [scale / aspect, 0, 0, 0, 0, scale, 0, 0, 0, 0, depth, -1, 0, 0, offset, 0];
    }

    /// <summary>Creates a current light identity whose large chunk origin tests relative-to-world reconstruction.</summary>
    private static ShadowLightSnapshot Lights(int originX, int originY, int originZ, Vector3 remainder)
    {
        var sun = new ShadowSourceKey(1, 0, ShadowSourceKind.Sun, 7, 7, 0);
        var moon = new ShadowSourceKey(1, 0, ShadowSourceKind.Moon, 8, 8, 0);
        return new(1, 1, 0, [], new(sun, Vector3.UnitZ, Vector3.One),
            new(moon, -Vector3.UnitZ, Vector3.One), Identity.ToImmutableArray(), originX, originY, originZ, remainder);
    }

    /// <summary>Checks every authored near/far perspective corner after an independent quarter-turn view transform.</summary>
    private static void AssertPerspectiveCorners(ShadowBounds bounds, ShadowLightSnapshot lights, double fov,
        double aspect, double near, double far)
    {
        double originX = lights.OriginX * 32.0 + lights.OriginRemainder.X;
        double originY = lights.OriginY * 32.0 + lights.OriginRemainder.Y;
        double originZ = lights.OriginZ * 32.0 + lights.OriginRemainder.Z;
        double tangent = Math.Tan(fov / 2);
        foreach (double distance in new[] { near, far })
        foreach (int horizontalSign in new[] { -1, 1 })
        foreach (int verticalSign in new[] { -1, 1 })
        {
            double viewX = horizontalSign * distance * tangent * aspect;
            double viewY = verticalSign * distance * tangent;
            // Inverse of the quarter-turn view maps (x,y,-distance) to (distance,y,x).
            var corner = new ShadowBounds(originX + distance, originY + viewY, originZ + viewX,
                originX + distance, originY + viewY, originZ + viewX);
            Assert.True(bounds.Contains(corner), $"Receiver bounds omitted the perspective corner {corner}.");
        }
    }
    #endregion
}
