using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.WorldProbes;

/// <summary>Checks CPU-side queue admission, capacity, level validation, and disposal behavior.</summary>
public sealed class WorldProbeTraceRoutingTests
{
    #region Public API
    /// <summary>Bounds queued rays and verifies admission does not claim work before dispatch.</summary>
    [Fact]
    public void ComputeBackendBoundsAdmissionWithoutClaimingQueuedWork()
    {
        // Arrange
        int claims=0;
        using var backend=new LumOnWorldProbeGpuTraceBackend(new Moq.Mock<Vintagestory.API.Common.ICoreAPI>().Object,256,
            ()=>null,()=>null,(_,_)=>{claims++;return true;},new VanillaGraphicsExpanded.Tests.Support.Lighting.UnexpectedWorldProbeTraceScene(),_=>true);
        var item=new LumOnWorldProbeTraceWorkItem(0,new(0,new(),new(),0),new(1,2,3),64,64,4096,false,.25f,-1,1e-6f);
        // Act
        var invalidOrigin = item with { ProbePosWorld=new(double.NaN,2,3) };
        // Assert
        Assert.Throws<ArgumentOutOfRangeException>(()=>backend.TryEnqueue(invalidOrigin));
        // Act
        bool firstAdmitted = backend.TryEnqueue(item);
        bool secondAdmitted = backend.TryEnqueue(item);
        bool capacityRejected = backend.TryEnqueue(item);
        // Assert
        Assert.Equal(0,claims);
        Assert.True(firstAdmitted);
        Assert.True(secondAdmitted);
        Assert.False(capacityRejected);
        Assert.Equal(0,claims);
        // Act
        bool wrongLevelRejected = backend.TryEnqueue(item with { Request=item.Request with { Level=1 } });
        // Assert
        Assert.False(wrongLevelRejected);
        // Act
        backend.Dispose();
        bool disposedAdmission = backend.TryEnqueue(item);
        bool disposedDequeue = backend.TryDequeueResult(out _);
        // Assert
        Assert.False(disposedAdmission);
        Assert.False(disposedDequeue);
    }
    #endregion
}
