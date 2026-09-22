using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using System.Collections.Concurrent;
using System.Reflection;
using VanillaGraphicsExpanded.LumOn.Scene;

using Vintagestory.API.Common;
using Vintagestory.Common;

namespace VanillaGraphicsExpanded.Tests.Fixtures.NearField;

/// <summary>Real engine palettes with scripted chunk lifetime and strictly bounded accessor calls.</summary>
internal sealed class NearFieldCaptureFixture
{
    public ChunkData Data { get; } = ChunkData.CreateNew(32, new ChunkDataPool(32, null!));
    public LumonSceneTraceSceneChunkVersionProvider Versions { get; } = new();
    public ConcurrentBag<int> CaptureThreads { get; } = new();
    public Dictionary<int, Block> Blocks { get; } = new() { [0] = new Block { BlockId = 0 } };
    public bool Loaded { get; set; } = true;
    public bool Disposed { get; set; }
    public Action? OnUnpack { get; set; }
    public Action? OnBlock { get; set; }
    public int ChunkLookups;
    public IWorldChunk Chunk { get; }
    public IBlockAccessor Accessor { get; }
    public TraceGeometryLightDecoder Lighting { get; } = new(
        Enumerable.Range(0, 32).Select(i => i / 31f).ToArray(),
        Enumerable.Range(0, 32).Select(i => i / 31f).ToArray(),
        Enumerable.Range(0, 64).Select(i => (byte)(i * 4)).ToArray(),
        Enumerable.Range(0, 8).Select(i => (byte)(i * 36)).ToArray());

    #region Fixture construction
    /// <summary>Rejects all per-voxel world queries; only initial/final chunk identity reads are accepted.</summary>
    public NearFieldCaptureFixture()
    {
        Chunk = Proxy<IWorldChunk>((method, _) => method.Name switch
        {
            "get_Disposed" => Disposed,
            "get_Data" => Data,
            "Unpack_ReadOnly" => Unpack(),
            _ => throw new InvalidOperationException(method.Name)
        });
        Accessor = Proxy<IBlockAccessor>((method, _) =>
        {
            if (method.Name != nameof(IBlockAccessor.GetChunkAtBlockPos)) throw new InvalidOperationException(method.Name);
            Interlocked.Increment(ref ChunkLookups);
            return Loaded ? Chunk : null;
        });
    }

    /// <summary>Creates the production snapshot source with the fixture's palettes and lifecycle controls.</summary>
    public TraceGeometrySnapshotSource CreateSource() => new(Accessor, id =>
    {
        OnBlock?.Invoke();
        return Blocks[id];
    }, Versions, new TraceGeometryMaterials(), Lighting);

    /// <summary>Records the executing thread and allows deterministic invalidation during capture.</summary>
    private bool Unpack()
    {
        CaptureThreads.Add(Environment.CurrentManagedThreadId);
        OnUnpack?.Invoke();
        return true;
    }

    /// <summary>Builds an interface adapter that fails on unexpected engine operations.</summary>
    private static T Proxy<T>(System.Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        T proxy = DispatchProxy.Create<T, CaptureApiProxy>();
        ((CaptureApiProxy)(object)proxy).Handler = handler;
        return proxy;
    }
    #endregion

    /// <summary>Dispatches only the scripted engine API surface.</summary>
    public class CaptureApiProxy : DispatchProxy
    {
        public System.Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        /// <summary>Forwards the actual method and argument array.</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }
}
