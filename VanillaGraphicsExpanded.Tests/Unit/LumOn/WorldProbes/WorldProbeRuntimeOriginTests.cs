using System.Numerics;
using Moq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks that render-origin rebasing keeps published world probes anchored.</summary>
public sealed class WorldProbeRuntimeOriginTests
{
    #region Render origin
    /// <summary>Fractional camera movement preserves absolute placement without mutating cached scheduler parameters.</summary>
    [Fact]
    public void RebasePreservesWorldPositionsAndCachedOrigins()
    {
        var api = new Mock<ICoreClientAPI>();
        api.SetupGet(value => value.Event).Returns(Mock.Of<IClientEventAPI>());
        api.SetupGet(value => value.Logger).Returns(Mock.Of<ILogger>());
        using var buffers = new LumOnWorldProbeClipmapBufferManager(api.Object, new VgeConfig());
        var published = new Vec3d(16777216.25, 32.5, -16777216.75);
        Vector3[] original = [new(-8.25f, -2.5f, 4.75f), new(-16.25f, -10.5f, -3.25f)];
        Vector3[] ring = [new(1, 2, 3), new(3, 2, 1)];
        buffers.UpdateRuntimeParams(published, new((float)published.X, (float)published.Y, (float)published.Z),
            2, 2, 8, original, ring);
        Assert.True(buffers.TryGetRuntimeParams(out _, out _, out _, out _, out _, out var cached, out var cachedRings));
        var snapshot = cached.ToArray();
        // Revisit the same camera after different origins to expose cumulative rebasing drift.
        foreach (double movement in new[] { .125, 32.25, -.5, .125 })
        {
            var camera = new Vec3d(published.X + movement, published.Y + 1.625, published.Z - movement);
            Assert.True(buffers.TryGetRuntimeParams(out var origin, out _, out float spacing, out int levels,
                out int resolution, out var rebased, out var rings, camera));
            Assert.Equal(camera, origin);
            Assert.Equal(2f, spacing);
            Assert.Equal(2, levels);
            Assert.Equal(8, resolution);
            Assert.Same(cachedRings, rings);
            for (int i = 0; i < original.Length; i++)
            {
                Assert.Equal(published.X + original[i].X, camera.X + rebased[i].X);
                Assert.Equal(published.Y + original[i].Y, camera.Y + rebased[i].Y);
                Assert.Equal(published.Z + original[i].Z, camera.Z + rebased[i].Z);
            }
            Assert.Equal(snapshot, cached);
        }
        Assert.True(buffers.TryGetRuntimeParams(out var unchanged, out _, out _, out _, out _, out var final, out _));
        Assert.Equal(published, unchanged);
        Assert.Same(cached, final);
        Assert.Equal(snapshot, final);
    }
    #endregion
}
