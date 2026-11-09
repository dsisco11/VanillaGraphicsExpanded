using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn;

/// <summary>Checks pending identity age and successful completion totals independently of GPU timing.</summary>
public sealed class SurfacePageProgressTests
{
    #region Public API
    #region Queue lifetime
    /// <summary>Repeated attempts retain the original start and each page contributes completion latency once.</summary>
    [Fact]
    public void RetriesPreserveAgeUntilSuccessfulCompletion()
    {
        // Arrange and act: retrying the same identity must retain its first observed time.
        var progress=new SurfacePageProgress();progress.Observe(1,17,3,100);progress.Observe(1,17,3,400);
        progress.Observe(2,18,3,300);
        // Assert pending age remains anchored to the first observation.
        Assert.Equal(2,progress.PendingCount);Assert.Equal(400,progress.OldestMilliseconds(500));
        // Act: complete the first page twice.
        progress.Complete(1,600);progress.Complete(1,700);
        // Assert that duplicate completion contributes latency only once.
        Assert.Equal(1,progress.Completed);Assert.Equal(500,progress.CompletionMilliseconds);
        Assert.Equal(1,progress.PendingCount);Assert.Equal(400,progress.OldestMilliseconds(700));
        // Act and assert completion of the remaining page.
        progress.Complete(2,800);Assert.Equal(2,progress.Completed);Assert.Equal(1000,progress.CompletionMilliseconds);
        Assert.Equal(0,progress.OldestMilliseconds(1000));
    }

    /// <summary>A reassigned identity restarts pending age without inventing a completed page.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReassignedIdentityCannotInheritPendingAge(bool generationChanges)
    {
        // Arrange: observe one pending identity.
        var progress=new SurfacePageProgress();progress.Observe(7,17,3,100);
        // Act: assign a new key or generation to the same physical page.
        progress.Observe(7,generationChanges?17ul:18ul,generationChanges?(ushort)4:(ushort)3,500);
        // Assert the new identity starts at its reassignment time and is not a completion.
        Assert.Equal(1,progress.PendingCount);Assert.Equal(100,progress.OldestMilliseconds(600));
        Assert.Equal(0,progress.Completed);
        // Act: complete the newly assigned identity.
        progress.Complete(7,650);
        // Assert completion latency starts from its reassignment time.
        Assert.Equal(150,progress.CompletionMilliseconds);
    }

    /// <summary>Eviction and explicit invalidation retire pending identities without recording useful progress.</summary>
    [Fact]
    public void EvictionIsNotCompletionAndClearStartsNewLifetime()
    {
        // Arrange
        var progress=new SurfacePageProgress();progress.Observe(1,17,3,100);progress.Observe(2,18,4,200);
        // Act: prune and remove pending identities.
        progress.Prune((page,key,generation)=>page==2&&key==18&&generation==4);
        // Assert pruning removes pending work without recording completion.
        Assert.Equal(1,progress.PendingCount);Assert.Equal(0,progress.Completed);
        // Act: remove the remaining identity and attempt to complete it.
        progress.Remove(2);progress.Complete(2,900);
        // Assert explicit removal does not count as completion.
        Assert.Equal(0,progress.Completed);
        // Act: create later progress and clear the new lifetime.
        progress.Observe(3,19,1,300);progress.Complete(3,500);progress.Observe(4,20,1,600);
        // Act: clear the lifetime while another identity is pending.
        progress.Clear();
        // Assert clear resets pending and aggregate progress.
        Assert.Equal(0,progress.PendingCount);Assert.Equal(0,progress.Completed);
        Assert.Equal(0,progress.CompletionMilliseconds);Assert.Equal(0,progress.OldestMilliseconds(1000));
    }
    #endregion
    #endregion
}
