using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.Tests.Unit;

/// <summary>Checks pending identity age and successful completion totals independently of GPU timing.</summary>
public sealed class SurfacePageProgressTests
{
    #region Queue lifetime
    /// <summary>Repeated attempts retain the original start and each page contributes completion latency once.</summary>
    [Fact]
    public void RetriesPreserveAgeUntilSuccessfulCompletion()
    {
        var progress=new SurfacePageProgress();progress.Observe(1,17,3,100);progress.Observe(1,17,3,400);
        progress.Observe(2,18,3,300);Assert.Equal(2,progress.PendingCount);Assert.Equal(400,progress.OldestMilliseconds(500));
        progress.Complete(1,600);progress.Complete(1,700);
        Assert.Equal(1,progress.Completed);Assert.Equal(500,progress.CompletionMilliseconds);
        Assert.Equal(1,progress.PendingCount);Assert.Equal(400,progress.OldestMilliseconds(700));
        progress.Complete(2,800);Assert.Equal(2,progress.Completed);Assert.Equal(1000,progress.CompletionMilliseconds);
        Assert.Equal(0,progress.OldestMilliseconds(1000));
    }

    /// <summary>Virtual key or slot generation changes restart physical page age without inventing a completed page.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ReassignedIdentityCannotInheritPendingAge(bool generationChanges)
    {
        var progress=new SurfacePageProgress();progress.Observe(7,17,3,100);
        progress.Observe(7,generationChanges?17ul:18ul,generationChanges?(ushort)4:(ushort)3,500);
        Assert.Equal(1,progress.PendingCount);Assert.Equal(100,progress.OldestMilliseconds(600));
        Assert.Equal(0,progress.Completed);progress.Complete(7,650);Assert.Equal(150,progress.CompletionMilliseconds);
    }

    /// <summary>Eviction and explicit invalidation retire pending identities without recording useful progress.</summary>
    [Fact]
    public void EvictionIsNotCompletionAndClearStartsNewLifetime()
    {
        var progress=new SurfacePageProgress();progress.Observe(1,17,3,100);progress.Observe(2,18,4,200);
        progress.Prune((page,key,generation)=>page==2&&key==18&&generation==4);
        Assert.Equal(1,progress.PendingCount);Assert.Equal(0,progress.Completed);
        progress.Remove(2);progress.Complete(2,900);Assert.Equal(0,progress.Completed);
        progress.Observe(3,19,1,300);progress.Complete(3,500);progress.Observe(4,20,1,600);
        progress.Clear();Assert.Equal(0,progress.PendingCount);Assert.Equal(0,progress.Completed);
        Assert.Equal(0,progress.CompletionMilliseconds);Assert.Equal(0,progress.OldestMilliseconds(1000));
    }
    #endregion
}
