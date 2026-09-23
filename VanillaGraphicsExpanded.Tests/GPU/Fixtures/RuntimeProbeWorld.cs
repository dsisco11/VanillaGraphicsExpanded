using System.Reflection;
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
    private readonly Block air = new() { BlockId=0 };
    private readonly IWorldChunk chunk = DispatchProxy.Create<IWorldChunk,LoadedChunkSentinel>();
    private int workerReads;
    public IBlockAccessor Accessor { get; }
    public int WorkerReads => Volatile.Read(ref workerReads);
    public bool WorkerWaiting => Volatile.Read(ref entered);

    #region Engine boundary
    /// <summary>Shares the exact block identity used by GPU geometry capture.</summary>
    public RuntimeProbeWorld(Block solid)
    {
        this.solid=solid;
        Accessor=RuntimeRenderEvents.Adapt<IBlockAccessor>(Invoke);
    }

    /// <summary>Supplies only the engine calls needed by production center validation and voxel traversal.</summary>
    private object? Invoke(MethodInfo method,object?[]? args)
    {
        switch(method.Name)
        {
            case "get_MapSizeY": return 256;
            case "GetChunkAtBlockPos": return chunk;
            case "GetRainMapHeightAt": return 40;
            case "GetLightLevel": return 0;
            case "GetLightRGBs": return new Vec4f(0,0,0,0);
            case "GetMostSolidBlock":
                if(Environment.CurrentManagedThreadId!=renderThread)
                {
                    Interlocked.Increment(ref workerReads);
                    lock(gate)
                    {
                        if(hold) Volatile.Write(ref entered,true);
                        while(hold) Monitor.Wait(gate);
                    }
                }
                var pos=(BlockPos)args![0]!;
                return pos.X<=0 || pos.X>=7 || pos.Y<=32 || pos.Y>=39 || pos.Z<=0 || pos.Z>=7 ? solid : air;
            default: throw new NotSupportedException("Probe world: "+method.Name);
        }
    }

    /// <summary>Holds the next actual worker read while render callbacks continue.</summary>
    public void HoldWorker() { lock(gate) { Volatile.Write(ref entered,false); hold=true; } }
    /// <summary>Releases delayed traversal, including retired requests that must never publish.</summary>
    public void ReleaseWorker() { lock(gate) { hold=false; Monitor.PulseAll(gate); } }
    /// <summary>Releases pending reads; the monitor remains valid for retired workers until they exit.</summary>
    public void Dispose() => ReleaseWorker();
    #endregion
}
