using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Numerics;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Verifies ring-slot history retirement without erasing overlapping probes or leaking GL state.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WorldProbeDirectionalHistoryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    #region Physical slot invalidation
    /// <summary>A full reset crosses the guaranteed X workgroup limit without leaving the final physical slots uncleared.</summary>
    [Fact]
    public void FullResetCoversMoreThan65535PhysicalSlots()
    {
        // Arrange
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        using var resources=new LumOnWorldProbeClipmapGpuResources(assets.Api,41,1,1);
        Fill(resources);
#if DEBUG
        long dispatches=resources.InvalidationDispatchCount;
#endif
        // Act
        resources.ClearAll();
#if DEBUG
        // Assert
        Assert.Equal(dispatches+1,resources.InvalidationDispatchCount);
#endif
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeRadianceAtlas, 0).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeVis0, 0).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeDist0, 0).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeMeta0, 0).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Resources cannot become visible when their history-clear program cannot load.</summary>
    [Fact]
    public void InvalidClearProgramRejectsResourceCreation()
    {
        // Arrange
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        assets.Overrides["shaders/lumon_worldprobe_history_clear.csh.spv"]=[];
        assets.Overrides["shaders/lumon_worldprobe_history_clear.csh"]=System.Text.Encoding.UTF8.GetBytes("invalid shader");
        // Act: request resource creation with invalid history-clear shader artifacts.
        // Assert: production creation rejects the unsupported program before publishing resources.
        Assert.Throws<InvalidOperationException>(()=>new LumOnWorldProbeClipmapGpuResources(assets.Api,2,1,4));
    }

    /// <summary>Duplicate overlapping requests clear once, an empty flush preserves new data, and full reset clears every target.</summary>
    [Fact]
    public void QueuedOverlapAndEmptyFlushPreserveNewHistoryUntilFullReset()
    {
        // Arrange
        // Queue overlapping boxes and verify coalescing before flush; then exercise an empty flush and an immediate full reset.
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        using var resources=new LumOnWorldProbeClipmapGpuResources(assets.Api,4,2,4);
#if DEBUG
        long initialDispatches=resources.InvalidationDispatchCount;
#endif
        Assert.Equal(0,resources.PendingInvalidationCount);
        Fill(resources);
        // Act
        resources.QueueClearLocalBox(0,new(),new(),new(2,1,1));
        // Assert
        Assert.Equal(12,resources.PendingInvalidationCount);
#if DEBUG
        Assert.Equal(initialDispatches,resources.InvalidationDispatchCount);
#endif
        // Act
        resources.QueueClearLocalBox(0,new(),new(1,0,0),new(3,1,1));
        // Act
        resources.QueueClearLocalBox(0,new(),new(),new(2,1,1));
        // Assert
        Assert.Equal(16,resources.PendingInvalidationCount);
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeRadianceAtlas, 1).AssertEqual();
        // Act
        resources.FlushHistoryInvalidation();
#if DEBUG
        // Assert
        Assert.Equal(initialDispatches+1,resources.InvalidationDispatchCount);
#endif
        Assert.Equal(0,resources.PendingInvalidationCount);
        var cleared=ImageAssertions.Predicates.Predicate(resources.ProbeRadianceAtlas, SurfacePredicates.WorldProbeDirectionalHistoryTests_QueuedOverlapAndEmptyFlushPreserveNewHistoryUntilFullReset);
        cleared.AssertEqual();Assert.Equal((uint)(16<<4),cleared.MatchingTexels);
        Fill(resources);
        // Act
        resources.FlushHistoryInvalidation();
#if DEBUG
        // Assert
        Assert.Equal(initialDispatches+1,resources.InvalidationDispatchCount);
#endif
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeRadianceAtlas, 1).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeMeta0, 1).AssertEqual();
        // Full reset must be immediate and independent of fixed-function write restrictions.
        GL.Enable(EnableCap.ScissorTest);GL.Scissor(0,0,1,1);GL.ColorMask(false,false,false,false);
        // Act
        try { resources.ClearAll(); }
        finally { GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true); }
        // Assert
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeRadianceAtlas, 0).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeMeta0, 0).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeVis0, 0).AssertEqual();
        ImageAssertions.Comparisons.CompareConstant(resources.ProbeDist0, 0).AssertEqual();
#if DEBUG
        Assert.Equal(initialDispatches+2,resources.InvalidationDispatchCount);
#endif
        Assert.Equal(0,resources.PendingInvalidationCount);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>A wrapped inclusive local box clears every directional and scalar target only in the selected level.</summary>
    [Fact]
    public void WrappedLocalBoxPreservesOtherSlotsAndRenderState()
    {
        // Arrange
        // Set hostile fixed-function state before regional invalidation, verify restoration, then inspect selected and untouched atlas slots.
        EnsureContextValid();
        using var assets=new BinaryShaderApiFixture();
        using var resources=new LumOnWorldProbeClipmapGpuResources(assets.Api,4,2,4);
        GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
        FramebufferTestSetup.ClearPass(resources.RadiancePass,1);
        FramebufferTestSetup.ClearPass(resources.MetadataPass,1);
        GL.Enable(EnableCap.ScissorTest);GL.Scissor(3,4,5,6);GL.ColorMask(false,true,false,true);
        try
        {
            // Act
            resources.QueueClearLocalBox(1,new VectorInt3(3,2,1),new VectorInt3(),new VectorInt3(2,1,1));
            // Act
            resources.FlushHistoryInvalidation();
            int[] scissor=new int[4];bool[] mask=new bool[4];
            GL.GetInteger(GetPName.ScissorBox,scissor);GL.GetBoolean(GetPName.ColorWritemask,mask);
            // Assert
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest));
            Assert.Equal(new[]{3,4,5,6},scissor);Assert.Equal(new[]{false,true,false,true},mask);
        }
        finally { GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true); }
        // Author expected coordinates independently of the invalidation shader, including every direction.
        var radiance = new List<Assertions.GpuVectorRegion>();
        var metadata = new List<Assertions.GpuVectorRegion>();
        for(int level=0;level<2;level++)for(int y=0;y<4;y++)for(int z=0;z<4;z++)
        {
            // Contiguous runs encode the same independent slot-membership oracle without repeated directional pixels.
            for (int x=0;x<4;)
            {
                bool cleared=level==1 && x!=2 && y>=2 && z>=1 && z<=2;
                int first=x++;
                while(x<4 && (level==1 && x!=2 && y>=2 && z>=1 && z<=2)==cleared)x++;
                var value=new System.Numerics.Vector4(cleared?0:1);
                radiance.Add(new(new(new(0),(z*4+first)*4,(level*4+y)*4,0,(x-first)*4,4),value));
                metadata.Add(new(new(new(0),z*4+first,level*4+y,0,x-first,1),value,1));
            }
        }
        ImageAssertions.Comparisons.CompareRegions(resources.ProbeRadianceAtlas,radiance).AssertEqual();
        ImageAssertions.Comparisons.CompareRegions(resources.ProbeMeta0,metadata).AssertEqual();
        ImageAssertions.Comparisons.CompareRegions(resources.ProbeVis0,metadata).AssertEqual();
        ImageAssertions.Comparisons.CompareRegions(resources.ProbeDist0,metadata).AssertEqual();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion

    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("predicate-surface", 0, 0);
    }
    #endregion

    #region Private
    /// <summary>Fills all production attachments with a visible sentinel before queueing invalidation.</summary>
    private static void Fill(LumOnWorldProbeClipmapGpuResources resources)
    {
        GL.Disable(EnableCap.ScissorTest);GL.ColorMask(true,true,true,true);
        FramebufferTestSetup.ClearPass(resources.RadiancePass,1);
        FramebufferTestSetup.ClearPass(resources.MetadataPass,1);
    }
    #endregion
}
