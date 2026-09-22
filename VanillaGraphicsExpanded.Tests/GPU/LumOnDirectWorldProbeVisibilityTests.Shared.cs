using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Shared publication controls for direct world-probe visibility consumers.</summary>
public sealed partial class LumOnDirectWorldProbeVisibilityTests
{
    #region Shared geometry visibility
    /// <summary>Rejects a shared wall on the first draw without any prior camera or publication history.</summary>
    [Fact]
    public void SharedInitiallyClosedWallRejectsCache()
    {
        EnsureShaderTestAvailable();
        using var shared = new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0, 32, -5), true, 32, 256),
            new(), (_, _, z) => new(z == -2 ? 2u : 1u, 0, 0));
        shared.Publish();
        var result = RenderDirectVisibility(CreateUniformCache(), null, new(.5f, .5f, -5), new(-7.5f), 16, -2,
            worldOffset: new(0, 32, 0), shared: shared.Scene);
        AssertLighting(result, -2, false);
    }

    /// <summary>Door edits, camera bob and ring movement preserve exact visibility at signed large coordinates.</summary>
    [Theory]
    [InlineData(31, 0)] [InlineData(-1, 0)] [InlineData(-2, 0)]
    [InlineData(31, -16777216)] [InlineData(-1, -16777216)] [InlineData(-2, -16777216)]
    [InlineData(31, 16777216)] [InlineData(-1, 16777216)] [InlineData(-2, 16777216)]
    public void SharedDoorwayEditsAndCameraMovement(int consumer, int anchor)
    {
        EnsureShaderTestAvailable();
        bool door = true;
        var plan = TraceGeometryCoverage.Plan(new(anchor, 32, -5), true, 32, 256);
        using var shared = new SharedTraceGeometryFixture(plan, new(), (x, y, z) =>
        {
            bool wall = x == anchor - 3 || x == anchor + 3 || y == 29 || y == 35 || z == -8 || z == -2;
            if (door && x == anchor && y == 32 && z == -2) wall = false;
            return new(wall ? 2u : 1u, 0, 0);
        });
        shared.Publish();
        Assert.Equal(1u, shared.ReadGeometry(anchor, 32, -2));
        Check(true, 0);
        Check(true, .2f);
        shared.Move(TraceGeometryCoverage.Plan(new(anchor + 16, 32, -5), true, 32, 256));
        shared.Publish();
        Check(true, -.2f);
        door = false; shared.Dirty();
        Check(false, 0); // Invalidated cells must not expose the old doorway before republishing.
        shared.Publish();
        Assert.Equal(2u, shared.ReadGeometry(anchor, 32, -2));
        Check(false, 0);
        Check(false, .2f);

        /// <summary>Evaluates the real consumer while keeping its physical receiver fixed during view bob.</summary>
        void Check(bool lit, float bob)
        {
            var result = RenderDirectVisibility(CreateUniformCache(), null,
                new Vector3(.5f, .5f, -5), new Vector3(-7.5f), 16, consumer,
                worldOffset: new VectorInt3(anchor, 32, 0), cameraBob: bob, shared: shared.Scene);
            AssertLighting(result, consumer, lit);
        }
    }
    #endregion
}
