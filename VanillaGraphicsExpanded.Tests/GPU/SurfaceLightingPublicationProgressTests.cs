using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures publication latency using production tile resolution and bounded renderer callbacks.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GPU")]
public sealed class SurfaceLightingPublicationProgressTests : RenderTestBase
{
    #region Construction
    /// <summary>Uses the material-isolated context for real capture and relight work.</summary>
    public SurfaceLightingPublicationProgressTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Production budgets
    /// <summary>Block-scale camera motion preserves seed progress within and across a physical ring boundary.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(15)]
    public void CameraMotion_PreservesPublicationProgress(int firstBlock)
    {
        EnsureContextValid();
        // Keep the authored face inside both logical domains when crossing the ring boundary.
        using var runtime = new SurfaceCacheRuntimeFixture(feedbackPlaneX: firstBlock == 15 ? 8 : 0);
        runtime.Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge = 4;
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 4;
        runtime.CameraX = firstBlock;
        runtime.PrimeGeometry();
        if (firstBlock == 15)
        {
            runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 0;
            runtime.RunUntil(runtime.AllRequestedCaptured);
            runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 4;
        }
        int movingPublications = 0;
        long firstRevision = runtime.Geometry.Resources!.InvalidationRevision;
        var origin = runtime.Geometry.Resources!.Coverage!.Window.Min;
        for (int frame = 0; frame < 32; frame++)
        {
            runtime.CameraX = firstBlock + frame % 2;
            runtime.Frame();
            if (runtime.TryGetLighting(out _)) movingPublications++;
        }
        long movingRevision = runtime.Geometry.Resources!.InvalidationRevision;
        int recoveryFrames = 0;
        while (recoveryFrames < 80 && !runtime.TryGetLighting(out _)) { runtime.Frame(); recoveryFrames++; }
        string observation = $"movingPublications={movingPublications}, revision={firstRevision}->{movingRevision}, physicalOrigin={origin}->{runtime.Geometry.Resources!.Coverage!.Window.Min}, staticRecoveryFrames={recoveryFrames}";
        TestContext.Current.TestOutputHelper!.WriteLine(observation);
        Assert.True(movingPublications > 0, observation);
        if (firstBlock == 1) Assert.Equal(origin, runtime.Geometry.Resources!.Coverage!.Window.Min);
        Assert.Equal(firstRevision, movingRevision);
        Assert.True(runtime.TryGetLighting(out _), observation);
    }

    /// <summary>New feedback residency preserves partial sweeps and already published unchanged pages.</summary>
    [Fact]
    public void GrowingResidency_PreservesPartialAndPublishedPages()
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(requestedPages: 48);
        runtime.VisibleFeedbackPages = 1;
        runtime.Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge = 4;
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 4;
        runtime.PrimeGeometry();
        long? dependency = null;
        int observedPublications = 0;
        for (int frame = 0; frame < 32; frame++)
        {
            runtime.VisibleFeedbackPages = frame + 1;
            runtime.Frame();
            bool available = runtime.TryGetLighting(out var snapshot);
            if (dependency.HasValue)
            {
                Assert.True(available, "Adding residency discarded previously published lighting.");
                Assert.Equal(dependency.Value, snapshot.DependencyRevision);
            }
            if (available) { dependency ??= snapshot.DependencyRevision; observedPublications++; }
        }
        Assert.True(observedPublications > 1, "Growing residency repeatedly discarded partial seed sweeps.");
    }

    /// <summary>Observes the first completed relight sweep as feedback introduces a finite page working set.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void FiniteWorkingSet_EventuallyPublishes(int texelsPerEdge)
    {
        EnsureContextValid();
        using var runtime = new SurfaceCacheRuntimeFixture(requestedPages: 48);
        runtime.Config.LumOn.LumonScene.NearTexelsPerVoxelFaceEdge = texelsPerEdge;
        runtime.Config.LumOn.LumonScene.RelightMaxPagesPerFrame = 4;
        runtime.PrimeGeometry();
        int firstPublished = -1, mappingChanges = 0, previousCount = 0, lastMappingChange = -1;
        int tileSize = 0;
        for (int frame = 0; frame < 260; frame++)
        {
            runtime.Frame();
            if (runtime.Feedback.TryGetNearDispatchState(out var pool, out _, out var mapping, out _))
            {
                tileSize = pool.Plan.TileSizeTexels;
                if (mapping.Count != previousCount)
                {
                    mappingChanges++; lastMappingChange = frame; previousCount = mapping.Count;
                }
            }
            if (runtime.TryGetLighting(out _)) { firstPublished = frame; break; }
        }
        ((LumonSceneRelightUpdateRenderer)runtime.LightingProvider).TryGetSelfCheckLine(out string progress);
        string observation = $"edge={texelsPerEdge}, tile={tileSize}, pages={previousCount}, mappingChanges={mappingChanges}, lastMappingChange={lastMappingChange}, firstPublished={firstPublished}, {progress}";
        TestContext.Current.TestOutputHelper!.WriteLine(observation);
        Assert.True(firstPublished >= 0, observation);
    }
    #endregion
}
