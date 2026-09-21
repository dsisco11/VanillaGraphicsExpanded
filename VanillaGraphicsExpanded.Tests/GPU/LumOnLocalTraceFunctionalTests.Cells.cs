using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.LocalTracing;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual sixteen-block GPU publication and identity ownership independently of lighting.</summary>
public sealed partial class LumOnLocalTraceFunctionalTests
{
    #region Cell publication
    /// <summary>The production cache and partition publish eight GPU subcells and reject a stale source revision.</summary>
    [Fact]
    public void ProductionPartition_PublishesAndInvalidatesGpuSubcells()
    {
        EnsureShaderTestAvailable();
        using var scene = new LocalTraceGpuScene(32);
        scene.SetWindow(default);
        int version = 1;
        var source = Enumerable.Repeat(new LocalTraceSourceCell(1, Vector4.Zero),32768).ToArray();
        var delayed = new List<(int Version, TaskCompletionSource<LocalTraceChunkSnapshot?> Completion)>();
        using var cache = new LocalTraceChunkCache((key, revision, cancellation) =>
        {
            if (revision == 1) return Task.FromResult<LocalTraceChunkSnapshot?>(new(key,revision,source));
            var completion = new TaskCompletionSource<LocalTraceChunkSnapshot?>();
            delayed.Add((revision,completion));
            return completion.Task;
        }, _ => version, _ => true);
        var provider = new LocalGeometryPartition(cache,scene,new LocalTraceMaterialRegistry());
        var limits = new PartitionLimits(8,8,8,8,long.MaxValue);
        var coordinator = new PartitionCoordinator(limits);
        long id = coordinator.Register("geometry","test",new(new(16,16,16)),new(0,0,0),limits,provider);
        coordinator.SetSource(new(1,id,"test",new(),new(new(),new(32,32,32))));
        cache.BeginFrame(new(new(0,0,0),new(2,2,2))); coordinator.Pump(0);
        Assert.Equal(1,cache.SourceReads); Assert.All(ReadReadiness(scene),x=>Assert.Equal(1,x));
        version=2;cache.BeginFrame(new(new(0,0,0),new(2,2,2)));provider.RefreshDependencies(coordinator);
        Assert.All(ReadReadiness(scene),x=>Assert.Equal(0,x));coordinator.Pump(1);
        version=3;delayed[0].Completion.SetResult(new(default,2,source));
        cache.BeginFrame(new(new(0,0,0),new(2,2,2)));provider.RefreshDependencies(coordinator);coordinator.Pump(2);
        Assert.All(ReadReadiness(scene),x=>Assert.Equal(0,x));
        delayed[1].Completion.SetResult(new(default,3,source));
        cache.BeginFrame(new(new(0,0,0),new(2,2,2)));provider.RefreshDependencies(coordinator);coordinator.Pump(3);
        Assert.All(ReadReadiness(scene),x=>Assert.Equal(1,x));
        coordinator.Unregister(id);
    }

    /// <summary>Published voxels outside the configured origin domain cannot masquerade as supported traces.</summary>
    [Fact]
    public void SupportedOriginDomain_RejectsOutsideProbeOrigins()
    {
        EnsureShaderTestAvailable();
        using var fixture = new Fixtures.LocalTraceVoxelFixture();
        fixture.Publish(new VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes.ControlledVoxelWorld());
        var allowed = new PartitionBounds(new(-1, -1, -6), new(1, 1, -4));
        Assert.True(Trace(fixture, supportedOrigins: allowed, maximumTraceReach: 32).Radiance[0] > 9);
        var excluded = new PartitionBounds(new(8, 8, 8), new(10, 10, 10));
        AssertUnresolved(Trace(fixture, supportedOrigins: excluded, maximumTraceReach: 32));
    }

    /// <summary>Readiness becomes visible with matching geometry and light, preserving X/Z/Y snapshot order.</summary>
    [Fact]
    public void CellPublication_PacksGeometryAndLightCoherently()
    {
        EnsureShaderTestAvailable();
        using var scene = new LocalTraceGpuScene(16);
        scene.SetWindow(default);
        var key = new PartitionCellKey(1, "test", new(0, 0, 0));
        var request = new PartitionRequest(1, key, 1, 1, 1, CancellationToken.None);
        var cells = Enumerable.Repeat(new LocalTraceSourceCell(1, Vector4.Zero), 4096).ToArray();
        cells[(3 * 16 + 5) * 16 + 7] = new(6, new Vector4(0.2f, 0.4f, 0.6f, 0.8f));
        Assert.True(scene.ClaimCell(request));
        Assert.Equal(new byte[] { 0 }, ReadReadiness(scene));
        Assert.True(scene.PublishCell(request, cells, new LocalTraceMaterialRegistry()));
        Assert.Equal(new byte[] { 1 }, ReadReadiness(scene));
        var geometry = new uint[4096];
        var light = new float[4096 * 4];
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, scene.Geometry.TextureId))
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, geometry);
        using (GlStateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, scene.Light.TextureId))
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.Rgba, PixelType.Float, light);
        int offset = (5 * 16 + 3) * 16 + 7;
        Assert.Equal(6u, geometry[offset]);
        for (int channel = 0; channel < 4; channel++) Assert.InRange(light[offset * 4 + channel], 0.2f * (channel + 1) - 0.002f, 0.2f * (channel + 1) + 0.002f);
        scene.InvalidateCell(key);
        Assert.Equal(new byte[] { 0 }, ReadReadiness(scene));
        Assert.False(scene.PublishCell(request, cells, new LocalTraceMaterialRegistry()));
    }

    /// <summary>A replaced request or departed cell cannot overwrite readiness owned by the current generation.</summary>
    [Fact]
    public void CellPublication_RejectsReplacedClaimsAndReusedSlots()
    {
        EnsureShaderTestAvailable();
        using var scene = new LocalTraceGpuScene(16);
        scene.SetWindow(default);
        var key = new PartitionCellKey(1, "test", new(0, 0, 0));
        var old = new PartitionRequest(1, key, 1, 1, 1, CancellationToken.None);
        var current = old with { Revision = 2, RequestId = 2 };
        var cells = Enumerable.Repeat(new LocalTraceSourceCell(1, Vector4.Zero), 4096).ToArray();
        var materials = new LocalTraceMaterialRegistry();
        Assert.True(scene.ClaimCell(old));
        scene.InvalidateCell(key);
        Assert.True(scene.ClaimCell(current));
        Assert.True(scene.PublishCell(current, cells, materials));
        long revision = scene.Revision;
        Assert.False(scene.PublishCell(old, cells, materials));
        Assert.Equal(revision, scene.Revision);
        scene.SetWindow(new VectorInt3(16, 0, 0));
        Assert.Equal(new byte[] { 0 }, ReadReadiness(scene));
        var next = new PartitionRequest(1, new(1, "test", new(1, 0, 0)), 2, 1, 3, CancellationToken.None);
        Assert.True(scene.ClaimCell(next));
        Assert.True(scene.PublishCell(next, cells, materials));
        scene.RetireCell(key);
        Assert.Equal(new byte[] { 1 }, ReadReadiness(scene));
        Assert.False(scene.ClaimCell(current));
        Assert.False(scene.PublishCell(current, cells, materials));
    }
    #endregion
}
