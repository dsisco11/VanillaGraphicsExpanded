using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

/// <summary>Prevents unavailable geometry in one texel bucket from starving the remainder of a surface page.</summary>
public sealed class LumonSceneRelightBatchScheduleTests
{
    /// <summary>Retiring one mapping restarts only that page while preserving an unrelated partial sweep.</summary>
    [Fact]
    public void RemovingOnePagePreservesOtherPartialSweeps()
    {
        var schedule = new LumonSceneRelightBatchSchedule();
        foreach (ulong page in new ulong[] { 1, 2 })
        {
            Assert.Equal(0u, schedule.Next(page, 2));
            Assert.False(schedule.Complete(page, 2, true));
        }
        schedule.Remove(1);
        Assert.Equal(0u, schedule.Next(1, 2));
        Assert.False(schedule.Complete(1, 2, true));
        Assert.Equal(1u, schedule.Next(2, 2));
        Assert.True(schedule.Complete(2, 2, true));
    }

    /// <summary>Mixed failed and valid buckets progress round-robin but never falsely finish a page.</summary>
    [Fact]
    public void FailedBucketDoesNotStarveValidBuckets()
    {
        var schedule = new LumonSceneRelightBatchSchedule();
        for (int i = 0; i < 10; i++)
        {
            uint bucket = schedule.Next(1, 3);
            Assert.Equal((uint)(i % 3), bucket);
            Assert.False(schedule.Complete(1, 3, bucket != 0));
        }
        // Geometry becomes ready: one full successful sweep now finishes the page.
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal((uint)((i + 1) % 3), schedule.Next(1, 3));
            Assert.Equal(i == 2, schedule.Complete(1, 3, true));
        }
    }

    /// <summary>Geometry history invalidation requires all buckets again, independently for each page.</summary>
    [Fact]
    public void HistoryResetDiscardsPartialSuccess()
    {
        var schedule = new LumonSceneRelightBatchSchedule();
        schedule.Next(1, 2); Assert.False(schedule.Complete(1, 2, true));
        schedule.Clear();
        Assert.Equal(0u, schedule.Next(1, 2));
        Assert.False(schedule.Complete(1, 2, true));
        Assert.Equal(0u, schedule.Next(2, 2));
        Assert.False(schedule.Complete(2, 2, false));
        Assert.Equal(1u, schedule.Next(1, 2));
        Assert.True(schedule.Complete(1, 2, true));
    }
}
