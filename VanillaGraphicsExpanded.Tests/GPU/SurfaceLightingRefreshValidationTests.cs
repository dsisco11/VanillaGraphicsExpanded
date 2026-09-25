using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks settled lighting transport through retained pages after localized source edits.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingRefreshValidationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Settled refresh
    /// <summary>A recycled physical page rejects its bright old contents until a different dark surface is captured and initialized.</summary>
    [Fact]
    public void ReusedPhysicalPageCannotPublishOldLightingBeforeCapture()
    {
        EnsureContextValid();
        using var runtime=new SurfaceCacheRuntimeFixture(exposedWall:true);
        bool missing=true;
        runtime.TransformVoxel=(x,y,z,voxel)=>voxel with
        {
            Geometry=x==32 && missing ? 0u : (x&31)==0 ? 2u|(runtime.MaterialId<<2) : 1u,
            LegacyLight=x>=32 ? 0u : voxel.LegacyLight
        };
        runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var initialPages,out _));
        uint physical=initialPages.Single().Key;
        Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(0,1,0),out uint oldSlot,out ushort oldGeneration));
        long oldCapture=runtime.Feedback.GetCaptureRevision(physical);
        Assert.True(runtime.TryGetLighting(out var initial));
        using(var pixels=SurfaceLightingPageReadback.Read(initial.OutgoingRadiance,initial,physical))
            Assert.True(pixels.Span[0]>.1f);
        runtime.CameraX=32; runtime.FeedbackChunk=new(1,1,0);
        for(int frame=0;frame<24;frame++)
        {
            runtime.Frame();
            Assert.False(runtime.AllRequestedCaptured());
            Assert.False(runtime.TryGetLighting(out _));
        }
        Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var recycled,out _));
        Assert.Equal(physical,recycled.Single().Key);
        Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(1,1,0),out uint newSlot,out ushort newGeneration));
        Assert.Equal(oldSlot,newSlot); Assert.NotEqual(oldGeneration,newGeneration);
        missing=false; runtime.InvalidateGeometry();
        runtime.RunUntil(runtime.AllRequestedLightingReady);
        Assert.True(runtime.TryGetLighting(out var current));
        Assert.NotEqual(oldCapture,runtime.Feedback.GetCaptureRevision(physical));
        Assert.Same(initial.IndirectIrradiance,current.IndirectIrradiance);
        using var dark=SurfaceLightingPageReadback.Read(current.OutgoingRadiance,current,physical);
        for(int index=0;index<dark.Length;index++) Assert.Equal((index&3)==3?1:0,dark.Span[index]);
    }

    /// <summary>Remote retained indirect lighting reaches the same energy as a fresh cache of the edited scene.</summary>
    [Fact]
    public void RemoteIndirectConvergesToFreshEditedSceneWithoutRecapture()
    {
        EnsureContextValid();
        ulong identity;
        float retained;
        using(var runtime=new SurfaceCacheRuntimeFixture(spatial:new SpatialLightingScene { Reflectance=.25f }))
        {
            runtime.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = runtime.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
            runtime.Config.LumOn.LumonScene.RelightRaysPerTexel=16;
            runtime.PrimeGeometry(); runtime.RunUntil(runtime.AllRequestedLightingReady);
            for(int frame=0;frame<256;frame++) runtime.Frame();
            Assert.True(runtime.Feedback.TryGetNearDispatchState(out _,out _,out var pages,out _));
            Assert.True(runtime.Feedback.TryGetNearChunkSlotAndGeneration(new(-1,1,0),out uint slot,out _));
            uint page=pages.Where(pair=>LumonSceneVirtualPageKeyUtil.UnpackChunkSlot(pair.Value)==slot)
                .OrderByDescending(pair=>Energy(runtime,pair.Key)).First().Key;
            identity=pages[page];
            float bright=Energy(runtime,page);
            Assert.True(bright>.1f);
            long capture=runtime.Feedback.GetCaptureRevision(page);
            Assert.True(runtime.TryGetLighting(out var original));
            runtime.TransformVoxel=(x,y,z,voxel)=>x>=0 ? voxel with { LegacyLight=0 } : voxel;
            foreach(var source in runtime.Sources.Where(source=>!source.Disposed))
                source.MarkDirty(ChunkKey.FromChunkCoords(0,1,0));
            for(int frame=0;frame<512;frame++)
            {
                runtime.Frame();
                Assert.True(runtime.TryGetLighting(out var current));
                Assert.Equal(original.DependencyRevision,current.DependencyRevision);
                Assert.Equal(capture,runtime.Feedback.GetCaptureRevision(page));
                using var pixels=SurfaceLightingPageReadback.Read(current.OutgoingRadiance,current,page);
                for(int index=3;index<pixels.Length;index+=4) Assert.Equal(1,pixels.Span[index]);
            }
            retained=Energy(runtime,page);
            Assert.True(retained<bright*.9f);
            for(int frame=0;frame<128;frame++) runtime.Frame();
            Assert.InRange(Math.Abs(Energy(runtime,page)-retained),0,Math.Max(.03f,retained*.05f));
        }
        // A separately initialized cache supplies the target; it never inherits pre-edit illumination.
        using var reference=new SurfaceCacheRuntimeFixture(spatial:new SpatialLightingScene { Reflectance=.25f });
        reference.Config.LumOn.LumonScene.RelightSeedPagesPerFrame = reference.Config.LumOn.LumonScene.RelightDirectPagesPerFrame = reference.Config.LumOn.LumonScene.RelightIndirectPagesPerFrame = 4;
        reference.Config.LumOn.LumonScene.RelightRaysPerTexel=16;
        reference.TransformVoxel=(x,y,z,voxel)=>x>=0 ? voxel with { LegacyLight=0 } : voxel;
        reference.PrimeGeometry(); reference.RunUntil(reference.AllRequestedLightingReady);
        for(int frame=0;frame<512;frame++) reference.Frame();
        Assert.True(reference.Feedback.TryGetNearDispatchState(out _,out _,out var mapping,out _));
        Assert.True(reference.Feedback.TryGetNearChunkSlotAndGeneration(new(-1,1,0),out uint referenceSlot,out _));
        ulong referenceIdentity=LumonSceneVirtualPageKeyUtil.Pack(referenceSlot,LumonSceneVirtualPageKeyUtil.UnpackVirtualPageIndex(identity));
        uint referencePage=mapping.Single(pair=>pair.Value==referenceIdentity).Key;
        float expected=Energy(reference,referencePage);
        Assert.True(reference.TryGetLighting(out var referenceLighting));
        using(var pixels=SurfaceLightingPageReadback.Read(referenceLighting.IndirectIrradiance,referenceLighting,referencePage))
        {
            bool accumulated=false;
            for(int index=3;index<pixels.Length;index+=4) accumulated|=pixels.Span[index]>=4;
            Assert.True(accumulated);
        }
        for(int frame=0;frame<128;frame++) reference.Frame();
        Assert.InRange(Math.Abs(Energy(reference,referencePage)-expected),0,Math.Max(.03f,expected*.05f));
        Assert.InRange(Math.Abs(retained-expected),0,Math.Max(.05f,expected*.1f));
    }
    #endregion

    #region Observations
    /// <summary>Averages red indirect irradiance over the page independently of the capped history count.</summary>
    private static float Energy(SurfaceCacheRuntimeFixture runtime,uint page)
    {
        Assert.True(runtime.TryGetLighting(out var snapshot));
        using var pixels=SurfaceLightingPageReadback.Read(snapshot.IndirectIrradiance,snapshot,page);
        float sum=0;
        for(int index=0;index<pixels.Length;index+=4) sum+=pixels.Span[index];
        return sum/(pixels.Length>>2);
    }
    #endregion
}
