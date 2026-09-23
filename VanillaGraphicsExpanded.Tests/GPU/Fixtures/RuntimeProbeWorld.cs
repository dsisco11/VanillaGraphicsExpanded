using Moq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Models a loaded enclosure and can delay real worker traversal at the engine block-access boundary.</summary>
internal sealed class RuntimeProbeWorld : IDisposable
{
    private readonly int renderThread = Environment.CurrentManagedThreadId;
    private readonly object gate = new();
    private bool hold, entered;
    private readonly Block solid;
    private readonly SpatialLightingScene? spatial;
    private readonly Block air = new() { BlockId=0 };
    private readonly IWorldChunk chunk = Mock.Of<IWorldChunk>();
    private int workerReads;
    public IBlockAccessor Accessor { get; }
    public int WorkerReads => Volatile.Read(ref workerReads);
    public bool WorkerWaiting => Volatile.Read(ref entered);

    #region Engine boundary
    /// <summary>Shares the exact block identity used by GPU geometry capture.</summary>
    public RuntimeProbeWorld(Block solid, SpatialLightingScene? spatial = null)
    {
        this.solid=solid; this.spatial=spatial;
        var accessor = new Mock<IBlockAccessor>(MockBehavior.Strict);
        accessor.SetupGet(api => api.MapSizeY).Returns(256);
        accessor.Setup(api => api.GetChunkAtBlockPos(It.IsAny<BlockPos>())).Returns((BlockPos location) =>
        {
            var key=VanillaGraphicsExpanded.Voxels.ChunkProcessing.ChunkKey.FromChunkCoords(location.X>>5,location.Y>>5,location.Z>>5);
            return spatial?.Loaded(key)==false ? null! : chunk;
        });
        accessor.Setup(api => api.GetRainMapHeightAt(It.IsAny<int>(),It.IsAny<int>())).Returns(40);
        accessor.Setup(api => api.GetLightLevel(It.IsAny<int>(),It.IsAny<int>(),It.IsAny<int>(),It.IsAny<EnumLightLevelType>())).Returns(0);
        accessor.Setup(api => api.GetLightRGBs(It.IsAny<BlockPos>())).Returns(new Vec4f(0,0,0,0));
        accessor.Setup(api => api.GetMostSolidBlock(It.IsAny<BlockPos>())).Returns(ReadBlock);
        Accessor=accessor.Object;
    }

    /// <summary>Delays actual worker reads at the engine boundary, then supplies the controlled scene's block.</summary>
    private Block ReadBlock(BlockPos pos)
    {
        if(Environment.CurrentManagedThreadId!=renderThread)
        {
            Interlocked.Increment(ref workerReads);
            lock(gate)
            {
                if(hold) Volatile.Write(ref entered,true);
                while(hold) Monitor.Wait(gate);
            }
        }
        return (spatial?.Solid(pos.X,pos.Y,pos.Z) ?? (pos.X<=0 || pos.X>=7 || pos.Y<=32 || pos.Y>=39 || pos.Z<=0 || pos.Z>=7)) ? solid : air;
    }
    /// <summary>Holds the next actual worker read while render callbacks continue.</summary>
    public void HoldWorker() { lock(gate) { Volatile.Write(ref entered,false); hold=true; } }
    /// <summary>Releases delayed traversal, including retired requests that must never publish.</summary>
    public void ReleaseWorker() { lock(gate) { hold=false; Monitor.PulseAll(gate); } }
    /// <summary>Releases pending reads; the monitor remains valid for retired workers until they exit.</summary>
    public void Dispose() => ReleaseWorker();
    #endregion
}
