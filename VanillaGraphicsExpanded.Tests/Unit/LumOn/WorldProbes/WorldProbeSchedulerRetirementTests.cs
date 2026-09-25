using VanillaGraphicsExpanded.LumOn.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks that routing retirement revokes admissions without invalidating unrelated retained probes.</summary>
public sealed class WorldProbeSchedulerRetirementTests
{
    /// <summary>Queued and running tickets become dirty and reschedulable while idle valid history stays valid.</summary>
    [Fact]
    public void RetirementRevokesOutstandingTicketsAndPreservesIdleValidState()
    {
        var scheduler=new LumOnWorldProbeScheduler(1,2);
        scheduler.UpdateOrigins(new(0,0,0),1);
        var all=scheduler.BuildUpdateList(0,new(0,0,0),1,[8],8,int.MaxValue,1);
        Assert.Equal(8,all.Count);
        scheduler.Complete(all[0],0,true);
        Assert.True(scheduler.TryClaim(all[1],0));
        scheduler.RetireOutstanding(1);
        var states=new LumOnWorldProbeLifecycleState[8];
        Assert.True(scheduler.TryCopyLifecycleStates(0,states));
        Assert.Equal(LumOnWorldProbeLifecycleState.Valid,states[all[0].StorageLinearIndex]);
        foreach(var old in all.Skip(1))
        {
            Assert.Equal(LumOnWorldProbeLifecycleState.Dirty,states[old.StorageLinearIndex]);
            Assert.False(scheduler.TryClaim(old,1));
            scheduler.Complete(old,1,true);
        }
        var replacement=scheduler.BuildUpdateList(1,new(0,0,0),1,[7],7,int.MaxValue,1);
        Assert.Equal(7,replacement.Count);
        foreach(var current in replacement)
        {
            var old=all.Single(item=>item.StorageLinearIndex==current.StorageLinearIndex);
            Assert.NotEqual(old.Ticket,current.Ticket);
            Assert.True(scheduler.TryClaim(current,1));
        }
    }
}
