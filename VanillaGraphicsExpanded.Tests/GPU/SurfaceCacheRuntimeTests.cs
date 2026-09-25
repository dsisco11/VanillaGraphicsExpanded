using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises renderer ownership through the callbacks registered with the engine.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceCacheRuntimeTests : RenderTestBase
{
    /// <summary>Uses the material-isolated graphics context.</summary>
    public SurfaceCacheRuntimeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Runtime ownership
    /// <summary>Disposal removes every registered callback, so retired renderers cannot run in the next world.</summary>
    [Fact]
    public void DisposedRenderersRemoveTheirEngineCallbacks()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        var api = RuntimeRenderEvents.Adapt<ICoreClientAPI>((method, args) => method.Name == "get_Event"
            ? events.Api : method.Invoke(assets.Api, args));
        var config = new VgeConfig();
        var partitions = new WorldPartitionModSystem();
        using var buffers = new GBufferManager(api);
        using var geometry = new TraceGeometryRenderer(api, config, partitions, _ => new RuntimeTraceGeometrySource((_, _, _) => default));
        var feedback = new LumonSceneFeedbackUpdateRenderer(api, config, buffers, partitions.GetCoordinator());
        var relight = new LumonSceneRelightUpdateRenderer(api, config, feedback, geometry);
        Assert.Equal(3, events.Registrations.Count);
        relight.Dispose(); feedback.Dispose(); geometry.Dispose();
        partitions.Dispose();
        Assert.Empty(events.Registrations);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Registered production callbacks populate, display, recreate and repopulate surface lighting across world lifetime changes.</summary>
    [Fact]
    public void RegisteredCallbacksDriveSurfaceCacheAcrossRecreationAndWorldRestart()
    {
        EnsureContextValid();
        var runtime = new SurfaceCacheRuntimeFixture();
        try
        {
            runtime.PrimeGeometry();
            runtime.RunUntil(runtime.SurfaceCacheSettled);
            Assert.Contains("vge_shared_trace_geometry", runtime.Events.Executed);
            Assert.Contains("vge_lumonscene_feedback", runtime.Events.Executed);
            Assert.Contains("vge_lumonscene_relight", runtime.Events.Executed);
            Assert.Contains("lumon_debug", runtime.Events.Executed);
            GpuTexture originalAtlas = Assert.IsAssignableFrom<GpuTexture>(runtime.IrradianceAtlas());

            runtime.RequestAtlasRecreation();
            runtime.RunUntil(() => runtime.IrradianceAtlas() is { } atlas && !ReferenceEquals(atlas, originalAtlas) && runtime.SurfaceCacheSettled());
            GpuTexture recreatedAtlas = Assert.IsAssignableFrom<GpuTexture>(runtime.IrradianceAtlas());
            Assert.NotSame(originalAtlas, recreatedAtlas);
            Assert.False(originalAtlas.IsValid);

            runtime.LeaveWorld();
            Assert.Null(runtime.Geometry.Resources);
            Assert.Equal(0, runtime.IrradianceAtlasId());
            Assert.All(runtime.Sources, source => Assert.True(source.Disposed));
            runtime.PrimeGeometry();
            runtime.RunUntil(() => runtime.Geometry.Resources is not null && runtime.IrradianceAtlasId() != 0 && runtime.SurfaceCacheSettled());
            Assert.True(recreatedAtlas.IsValid);
            Assert.True(runtime.Sources.Count >= 2);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            runtime.Dispose();
        }
        Assert.Empty(runtime.Events.Registrations);
        Assert.Equal(0, runtime.Events.SubscriptionCount("LeaveWorld"));
    }

    /// <summary>Confirms geometry published by the runtime renderer satisfies the established capture contract.</summary>
    [Fact]
    public void RuntimePublishedGeometrySupportsSurfaceCapture()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture();
        for (int frame = 0; frame < 20; frame++) runtime.Frame();
        TraceGeometryGpuScene scene = Assert.IsType<TraceGeometryGpuScene>(runtime.Geometry.Resources);
        using var page = new SharedSurfacePageFixture();
        Assert.True(page.CaptureWithProductionOwner(runtime.Api, scene));
        Assert.True(page.Capture(scene));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Sampling changes invalidate publication immediately and reseed valid darkness within the configured page budget.</summary>
    [Fact]
    public void PublicationRejectsChangedSettingsAndResourcesBeforeReuse()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture();
        runtime.PrimeGeometry();
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var first));
        runtime.Config.LumOn.LumonScene.RelightRaysPerTexel = 4;
        Assert.False(runtime.TryGetLighting(out _));
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var second));
        Assert.True(second.Generation > first.Generation);
        Assert.NotEqual(first.DependencyRevision, second.DependencyRevision);
        runtime.RequestAtlasRecreation();
        runtime.Frame();
        runtime.RunUntil(() => runtime.TryGetLighting(out var value) && !ReferenceEquals(value.OutgoingRadiance, second.OutgoingRadiance));
        Assert.False(second.OutgoingRadiance.IsValid);
        runtime.LeaveWorld();
        Assert.False(runtime.TryGetLighting(out _));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Partial publication exposes only initialized texels and carries them forward while another page starts.</summary>
    [Fact]
    public void PartialPagePublicationCarriesForwardUnchangedTiles()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(requestedPages:2,exposedWall:true);
        runtime.Config.LumOn.LumonScene.RelightTexelsPerPagePerFrame=4;
        runtime.PrimeGeometry();
        runtime.RunUntil(() => runtime.TryGetLighting(out _));
        Assert.True(runtime.TryGetLighting(out var first));
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var mappings,out _));
        uint[] ids=mappings.Keys.ToArray();
        Assert.Equal(2,ids.Length);
        int readCount=checked((int)ids.Max()+1);
        uint firstId;
        using (var ready=first.Readiness.MapRange<uint>(0,readCount,MapBufferAccessMask.MapReadBit))
        {
            Assert.True(ready.IsMapped);
            Assert.Equal(1u,ready.Span[(int)ids[0]]+ready.Span[(int)ids[1]]);
            firstId=ready.Span[(int)ids[0]]==1 ? ids[0] : ids[1];
        }
        float[] before=ReadTile(first,firstId);
        Assert.True(before[0]>0);
        Assert.Equal(4,Enumerable.Range(0,before.Length/4).Count(index=>before[index*4+3]==1));
        Assert.All(Enumerable.Range(0,before.Length/4),index=>Assert.True(before[index*4+3] is 0 or 1));
        foreach(int index in Enumerable.Range(0,before.Length/4).Where(index=>before[index*4+3]==0))
            for(int channel=0;channel<4;channel++) Assert.Equal(0,before[index*4+channel]);
        runtime.Frame();
        Assert.True(runtime.TryGetLighting(out var second));
        Assert.True(second.Generation>first.Generation);
        Assert.Equal(first.DependencyRevision,second.DependencyRevision);
        using (var ready=second.Readiness.MapRange<uint>(0,readCount,MapBufferAccessMask.MapReadBit))
        { Assert.True(ready.IsMapped);Assert.Equal(1u,ready.Span[(int)ids[0]]);Assert.Equal(1u,ready.Span[(int)ids[1]]); }
        Assert.Equal(before,ReadTile(second,firstId));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Reads one published tile using its production physical-address layout.</summary>
    private static float[] ReadTile(in SurfaceLightingSnapshot snapshot,uint id)
    {
        var atlas=snapshot.OutgoingRadiance;
        var all=new float[atlas.Width*atlas.Height*atlas.Depth*4];
        using var binding=GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray,0,atlas.TextureId);
        GL.GetTexImage(TextureTarget.Texture2DArray,0,PixelFormat.Rgba,PixelType.Float,all);
        int local=(int)((id-1)%(uint)snapshot.TilesPerAtlas), layer=(int)((id-1)/(uint)snapshot.TilesPerAtlas);
        int x=local%snapshot.TilesPerAxis*snapshot.TileSize,y=local/snapshot.TilesPerAxis*snapshot.TileSize;
        var tile=new float[snapshot.TileSize*snapshot.TileSize*4];
        for(int row=0;row<snapshot.TileSize;row++)
            all.AsSpan(((layer*atlas.Height+y+row)*atlas.Width+x)*4,snapshot.TileSize*4).CopyTo(tile.AsSpan(row*snapshot.TileSize*4));
        return tile;
    }
    #endregion
}
