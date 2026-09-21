using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Common;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises direct visibility across cache levels and moving local geometry windows.</summary>
public sealed partial class LumOnDirectWorldProbeVisibilityTests
{
    #region Clipmap Transitions
    /// <summary>Distinct level colors expose incorrect selection, blending, ring addressing, or wall acceptance.</summary>
    [Theory]
    [InlineData(31, 0)]
    [InlineData(-1, 0)]
    [InlineData(-2, 0)]
    [InlineData(31, 1)]
    [InlineData(31, 2)]
    [InlineData(31, 3)]
    [InlineData(-1, 1)]
    [InlineData(-1, 2)]
    [InlineData(-1, 3)]
    [InlineData(-2, 1)]
    [InlineData(-2, 2)]
    [InlineData(-2, 3)]
    public void ClipmapTransition_PreservesVisibleLevels_AndRejectsBlockedLevels(int consumer, int scenario)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        if (scenario is 1 or 2)
            for (int x = -28; x <= 28; x++)
            for (int y = -28; y <= 28; y++)
                world.SetBlock(x, y, scenario == 1 ? 0 : 2, new Block { BlockId = 1 });
        using var local = new LocalTraceVoxelFixture();
        local.Publish(world);
        Vector3[] origins = [new(-12), new(-24)];
        Vector3[] rings = [new(3, 2, 1), new(5, 1, 4)];
        var atlas = CreateTransitionCache(rings);
        if (scenario == 3) Array.Clear(atlas.Metadata, 0, atlas.ScalarWidth * atlas.Resolution * 2);
        float previousRed = float.PositiveInfinity;
        float previousGreen = -1;
        // Cross the fine interior, overlap band, and outer boundary.
        foreach (float x in new[] { 0f, 2f, 4f, 6f, 11.75f, 12.25f })
        {
            var pixels = RenderDirectVisibility(atlas, local.Scene, new Vector3(x, 0, -0.25f),
                origins[0], 2, consumer, size: 1, span: 0, levelOrigins: origins, levelRings: rings);
            if (scenario == 1 || (scenario == 2 && x > 12))
            {
                AssertLighting(pixels, consumer, false);
                continue;
            }
            float red = pixels[0], green = pixels[1];
            if (consumer >= 0)
            {
                red /= 1 - red;
                green /= 1 - green;
            }
            Assert.InRange(red + green, MathF.PI - 0.025f, MathF.PI + 0.025f);
            Assert.Equal(0, pixels[2]);
            if (scenario == 2)
            {
                Assert.InRange(red, MathF.PI - 0.025f, MathF.PI + 0.025f);
                Assert.Equal(0, green);
                continue;
            }
            if (scenario == 3)
            {
                Assert.Equal(0, red);
                Assert.InRange(green, MathF.PI - 0.025f, MathF.PI + 0.025f);
                continue;
            }
            Assert.True(red <= previousRed + 0.025f);
            Assert.True(green >= previousGreen - 0.025f);
            if (x == 0) Assert.InRange(red, MathF.PI - 0.025f, MathF.PI + 0.025f);
            if (x == 4) Assert.True(red > 0.2f && green > 0.2f);
            if (x > 12) Assert.InRange(green, MathF.PI - 0.025f, MathF.PI + 0.025f);
            previousRed = red;
            previousGreen = green;
        }
    }

    /// <summary>Packs red fine and green coarse probes, with only centers above the test wall published.</summary>
    private static WorldProbeAtlasData CreateTransitionCache(Vector3[] rings)
    {
        var atlas = new WorldProbeAtlasData(12, 16, 2);
        for (int level = 0; level < 2; level++)
        for (int z = 0; z < 12; z++)
        for (int y = 0; y < 12; y++)
        for (int x = 0; x < 12; x++)
        {
            int sx = (x + (int)rings[level].X) % 12;
            int sy = (y + (int)rings[level].Y) % 12;
            int sz = (z + (int)rings[level].Z) % 12;
            int u = sx + sz * 12, v = sy + level * 12;
            // Below-wall centers are deliberately unavailable, so all accepted segments
            // in the closed control must cross the wall regardless of selected level.
            atlas.Metadata[(v * atlas.ScalarWidth + u) * 2] = z >= 6 ? 1 : 0;
            for (int ty = 0; ty < 16; ty++)
            for (int tx = 0; tx < 16; tx++)
            {
                int i = ((v * 16 + ty) * atlas.Width + u * 16 + tx) * 4;
                atlas.Radiance[i + level] = 1;
                atlas.Radiance[i + 3] = MathF.Log(65);
            }
        }
        return atlas;
    }
    #endregion

    #region Local Window Lifetime
    /// <summary>Known clear segments stop contributing when either endpoint lies beyond published geometry.</summary>
    [Theory]
    [InlineData(31)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void LocalWindowBoundary_RejectsOutsideProbeAndReceiver(int consumer)
    {
        EnsureShaderTestAvailable();
        using var local = new LocalTraceVoxelFixture();
        local.Publish(new ControlledVoxelWorld());
        var atlas = CreateUniformCache();
        foreach (int side in new[] { -1, 1 })
        {
            float inside = side < 0 ? -31.5f : 31.5f;
            float outside = side < 0 ? -32.5f : 32.5f;
            foreach (var pair in new[] { (inside, inside, true), (inside, outside, false), (outside, inside, false) })
            {
                var center = new Vector3(pair.Item1, 0.5f, 0.5f);
                var receiver = new Vector3(pair.Item2, 0.5f, 0.5f);
                var pixels = RenderDirectVisibility(atlas, local.Scene, receiver,
                    center - new Vector3(64), 128, consumer, size: 1, span: 0);
                AssertLighting(pixels, consumer, pair.Item3);
            }
        }
    }

    /// <summary>Moving the geometry ring preserves overlap and rejects reused clear cells until their new owner is published.</summary>
    [Theory]
    [InlineData(31)]
    [InlineData(-1)]
    [InlineData(-2)]
    public void LocalWindowMovement_DoesNotExposeStaleClearGeometry(int consumer)
    {
        EnsureShaderTestAvailable();
        using var local = new LocalTraceVoxelFixture();
        local.Publish(new ControlledVoxelWorld());
        var atlas = CreateUniformCache();
        AssertAt(0.5f, true);
        local.MoveCenter(new VectorInt3(32, 0, 0));
        AssertAt(0.5f, true);
        AssertAt(32.5f, false);
        // The reused physical slot previously held air; its new owner contains a wall.
        var next = new ControlledVoxelWorld();
        next.SetBlock(32, 0, 0, new Block { BlockId = 1 });
        local.Publish(next);
        AssertAt(32.5f, false);
        local.Publish(new ControlledVoxelWorld());
        AssertAt(32.5f, true);

        /// <summary>Checks coincident probe and receiver ownership at the requested world cell.</summary>
        void AssertAt(float x, bool lit)
        {
            var center = new Vector3(x, 0.5f, 0.5f);
            var pixels = RenderDirectVisibility(atlas, local.Scene, center,
                center - new Vector3(8), 16, consumer, size: 1, span: 0);
            AssertLighting(pixels, consumer, lit);
        }
    }
    #endregion
}
