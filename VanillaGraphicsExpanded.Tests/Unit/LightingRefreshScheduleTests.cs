using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Verifies bounded operation and bucket fairness independently of GPU completion.</summary>
public sealed class LightingRefreshScheduleTests
{
    #region Scheduling
    /// <summary>Default near pages require four indirect buckets interleaved with four direct visits.</summary>
    [Fact]
    public void DefaultNearPageNeedsEightVisitsForOneIndirectSweep()
    {
        var config=new VanillaGraphicsExpanded.LumOn.VgeConfig.LumOnSettingsConfig.LumonSceneConfig();
        var plan=LumonScenePhysicalPoolPlanner.CreateNearPlan(config.NearTexelsPerVoxelFaceEdge,
            config.NearRadiusChunks,config.NearRadiusYChunks,config.NearPagesPerChunkBudget,1);
        Assert.Equal(16,plan.TileSizeTexels);
        Assert.Equal(64,config.RelightTexelsPerPagePerFrame);Assert.Equal(4,config.RelightMaxPagesPerFrame);
        int buckets=(plan.TileSizeTexels*plan.TileSizeTexels)/config.RelightTexelsPerPagePerFrame;
        var schedule=new LumonSceneLightingRefreshSchedule();var indirect=new HashSet<uint>();
        for(int visit=0;visit<8;visit++)
        {
            uint operation=schedule.Select(1,true,true,buckets,out uint bucket);
            Assert.Equal((visit&1)==0?1u:4u,operation);
            if(operation==1)Assert.True(indirect.Add(bucket));
        }
        Assert.Equal(new uint[]{0,1,2,3},indirect.Order().ToArray());
        Assert.Equal(1u,schedule.Select(1,true,true,buckets,out uint next));Assert.Equal(0u,next);
    }

    /// <summary>Each published page visits every direct and indirect bucket even when no operation resolves.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void PublishedPagesVisitAllBuckets(bool seeded)
    {
        var schedule=new LumonSceneLightingRefreshSchedule();
        int cycle=seeded?2:3;
        for(int visit=0;visit<cycle*8;visit++)
        {
            uint operation=schedule.Select(7,seeded,true,4,out uint bucket);
            int turn=visit%cycle;
            Assert.Equal(turn==0?1u:turn==1?4u:0u,operation);
            if(operation!=0) Assert.Equal((uint)((visit/cycle)%4),bucket);
        }
    }

    /// <summary>Unpublished work cannot skip initialization and retiring one page preserves unrelated cursors.</summary>
    [Fact]
    public void InitializationAndIdentityRetirementAreIndependent()
    {
        var schedule=new LumonSceneLightingRefreshSchedule();
        for(int visit=0;visit<5;visit++) Assert.Equal(0u,schedule.Select(1,false,false,4,out _));
        Assert.Equal(1u,schedule.Select(1,true,true,4,out _));
        Assert.Equal(1u,schedule.Select(2,true,true,4,out _));
        schedule.Remove(1);
        Assert.Equal(1u,schedule.Select(1,true,true,4,out uint reset));
        Assert.Equal(0u,reset);
        Assert.Equal(4u,schedule.Select(2,true,true,4,out _));
        schedule.Clear();
        Assert.Equal(1u,schedule.Select(2,true,true,4,out reset));
        Assert.Equal(0u,reset);
    }
    #endregion
}
