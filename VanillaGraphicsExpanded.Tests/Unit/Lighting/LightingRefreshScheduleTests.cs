using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit.Lighting;

/// <summary>Verifies independent bucket fairness and bounded traversal retry delays.</summary>
public sealed class LightingRefreshScheduleTests
{
    #region Public API
    #region Scheduling
    /// <summary>Direct traffic cannot consume indirect turns or require alternating visits.</summary>
    [Fact]
    public void IndependentStagesCompleteTheirOwnBucketSweeps()
    {
        // Arrange
        var schedule = new LumonSceneLightingRefreshSchedule();
        // Act and assert each stage's cursor independently.
        for (int visit = 0; visit < 12; visit++)
        {
            Assert.True(schedule.TryNext(7, false, 4, visit, out uint direct));
            Assert.Equal((uint)(visit % 4), direct);
            if (visit % 3 != 0) continue;
            Assert.True(schedule.TryNext(7, true, 4, visit, out uint indirect));
            Assert.Equal((uint)(visit / 3), indirect);
        }
        Assert.True(schedule.TryNext(7, true, 4, 13, out uint next));
        Assert.Equal(0u, next);
    }

    /// <summary>Retiring one identity resets only its own direct and indirect cursors.</summary>
    [Fact]
    public void IdentityRetirementPreservesUnrelatedCursors()
    {
        // Arrange
        var schedule = new LumonSceneLightingRefreshSchedule();
        // Act
        foreach (uint page in new uint[] { 1, 2 })
            foreach (bool indirect in new[] { false, true })
                Assert.True(schedule.TryNext(page, indirect, 4, 0, out _));
        schedule.Remove(1);
        // Assert retirement resets its cursor without changing another page's progress.
        foreach (bool indirect in new[] { false, true })
        {
            Assert.True(schedule.TryNext(1, indirect, 4, 1, out uint reset)); Assert.Equal(0u, reset);
            Assert.True(schedule.TryNext(2, indirect, 4, 1, out uint retained)); Assert.Equal(1u, retained);
        }
        // Act: clear all cursor state.
        schedule.Clear();
        // Assert clearing restarts the retained page's cursor too.
        Assert.True(schedule.TryNext(2, true, 4, 2, out uint cleared)); Assert.Equal(0u, cleared);
    }
    #endregion

    #region Exhaustion retry delays
    /// <summary>Exhausted indirect buckets yield to siblings and become eligible at their bounded deadline.</summary>
    [Fact]
    public void ExhaustedBucketDoesNotStarveOtherWork()
    {
        // Arrange
        var schedule = new LumonSceneLightingRefreshSchedule();
        // Act
        schedule.RecordExhaustion(7, 0, 10);
        var seen = new HashSet<uint>();
        for (int visit = 0; visit < 12; visit++)
        {
            Assert.True(schedule.TryNext(7, true, 4, 11, out uint bucket));
            Assert.NotEqual(0u, bucket); seen.Add(bucket);
        }
        // Assert
        Assert.Equal(new uint[] { 1, 2, 3 }, seen.Order().ToArray());
        // Act: retry the delayed bucket after its deadline.
        bool retried = false;
        for (int visit = 0; visit < 4; visit++)
            retried |= schedule.TryNext(7, true, 4, 42, out uint bucket) && bucket == 0;
        // Assert
        Assert.True(retried);
    }

    /// <summary>Fully delayed indirect work consumes no admission while direct work remains eligible.</summary>
    [Fact]
    public void WakeRestoresDelayedBucketsWithoutBlockingDirect()
    {
        // Arrange
        var schedule = new LumonSceneLightingRefreshSchedule(); schedule.RecordExhaustion(1, 0, 0);
        // Act and assert direct work remains eligible while the indirect bucket waits.
        Assert.False(schedule.TryNext(1, true, 1, 1, out _));
        Assert.True(schedule.TryNext(1, false, 1, 1, out _));
        // Act: clear the retry delay, then assert the bucket is eligible again.
        schedule.ClearRetryDelays();
        Assert.Equal(0, schedule.RetryCount);
        Assert.True(schedule.TryNext(1, true, 1, 2, out _));
    }

    /// <summary>Delay arithmetic remains valid across signed frame-counter wraparound.</summary>
    [Fact]
    public void RetryDeadlineSurvivesSignedFrameWrap()
    {
        // Arrange
        var schedule = new LumonSceneLightingRefreshSchedule();
        int start = int.MaxValue - 10;
        schedule.RecordExhaustion(1, 0, start);
        // Act and assert eligibility immediately before and at the wrapped deadline.
        Assert.False(schedule.TryNext(1, true, 1, unchecked(start + 31), out _));
        Assert.True(schedule.TryNext(1, true, 1, unchecked(start + 32), out _));
    }

    /// <summary>Repeated notifications remain bounded and identity retirement releases its delays.</summary>
    [Fact]
    public void RetryStorageIsBoundedAndRetiredWithIdentity()
    {
        // Arrange
        var schedule = new LumonSceneLightingRefreshSchedule();
        // Act
        for (uint page = 1; page <= 5000; page++)
        { schedule.RecordExhaustion(page, 0, 0); schedule.RecordExhaustion(page, 0, 1); }
        // Assert
        Assert.Equal(LumonSceneLightingRefreshSchedule.MaximumRetryEntries, schedule.RetryCount);
        Assert.True(schedule.TryNext(1, true, 1, 2, out _));
        // Act and assert retirement frees one bounded retry slot.
        schedule.Remove(5000);
        Assert.Equal(LumonSceneLightingRefreshSchedule.MaximumRetryEntries - 1, schedule.RetryCount);
        Assert.True(schedule.TryNext(5000, true, 1, 2, out _));
        // Act and assert clearing releases all retry state.
        schedule.Clear();
        Assert.Equal(0, schedule.RetryCount);
    }
    #endregion
    #endregion
}
