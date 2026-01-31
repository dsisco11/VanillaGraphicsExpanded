using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.LumOn.Scene;

public sealed class LumonSceneTraceSceneChunkSnapshotSourceTests
{
    [Fact]
    public async Task TryCreateSnapshotAsync_WhenChunkAvailable_UsesBulkReadLockAndReturnsSnapshot()
    {
        var versionProvider = new LumonSceneTraceSceneChunkVersionProvider();
        var lightIds = new LumonSceneTraceSceneLightIdRegistry();

        int[] blocks = new int[LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount];
        blocks[0] = 1;
        blocks[1] = 0;

        byte[] blockLight = new byte[blocks.Length];
        byte[] sunLight = new byte[blocks.Length];

        IChunkBlocks chunkBlocks = TestChunkBlocksProxy.Create(blocks, out TestChunkBlocksProxy blocksProxy);
        IChunkLight chunkLight = TestChunkLightProxy.Create(blockLight, sunLight, out _);
        IWorldChunk chunk = TestWorldChunkProxy.Create(chunkBlocks, chunkLight, unpackResult: true, disposed: false, out TestWorldChunkProxy chunkProxy);

        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            getChunk: (_, _, _) => chunk,
            getLightRgb: (_, _, _) => 0);

        var world = FunctionalClientWorldAccessorProxy.Create(
            blockAccessor: blockAccessor,
            getBlockById: id => id == 1 ? TestBlocks.SolidFull : TestBlocks.Air);

        var events = FunctionalClientEventApiProxy.Create(runMainThreadTaskInline: true);

        ICoreClientAPI capi = FunctionalCoreClientApiProxy.Create(events: events, world: world);

        var src = new LumonSceneTraceSceneChunkSnapshotSource(capi, versionProvider, lightIds);

        ChunkKey key = ChunkKey.FromChunkCoords(0, 0, 0);
        int version = versionProvider.GetCurrentVersion(key);

        using IChunkSnapshot? snapshot = await src.TryCreateSnapshotAsync(key, version, CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(32, snapshot!.SizeX);
        Assert.Equal(32, snapshot.SizeY);
        Assert.Equal(32, snapshot.SizeZ);

        Assert.True(chunkProxy.UnpackReadOnlyCalled);
        Assert.Equal(1, blocksProxy.TakeBulkReadLockCalls);
        Assert.Equal(1, blocksProxy.ReleaseBulkReadLockCalls);

        var typed = Assert.IsType<PooledChunkSnapshot<LumonSceneTraceSceneSourceCell>>(snapshot);
        Assert.Equal(1, typed.Voxels.Span[0].IsSolid);
        Assert.Equal(0, typed.Voxels.Span[1].IsSolid);
    }

    [Fact]
    public async Task TryCreateSnapshotAsync_WhenBlocklightNonZero_AssignsLightIdFromRgb()
    {
        var versionProvider = new LumonSceneTraceSceneChunkVersionProvider();
        var lightIds = new LumonSceneTraceSceneLightIdRegistry();

        int[] blocks = new int[LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount];
        blocks[0] = 1;

        byte[] blockLight = new byte[blocks.Length];
        byte[] sunLight = new byte[blocks.Length];
        blockLight[0] = 5;

        IChunkBlocks chunkBlocks = TestChunkBlocksProxy.Create(blocks, out _);
        IChunkLight chunkLight = TestChunkLightProxy.Create(blockLight, sunLight, out _);
        IWorldChunk chunk = TestWorldChunkProxy.Create(chunkBlocks, chunkLight, unpackResult: true, disposed: false, out _);

        int getLightRgbCalls = 0;
        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            getChunk: (_, _, _) => chunk,
            getLightRgb: (_, _, _) =>
            {
                getLightRgbCalls++;
                return 0x00112233;
            });

        var world = FunctionalClientWorldAccessorProxy.Create(
            blockAccessor: blockAccessor,
            getBlockById: id => id == 1 ? TestBlocks.SolidFull : TestBlocks.Air);

        var events = FunctionalClientEventApiProxy.Create(runMainThreadTaskInline: true);
        ICoreClientAPI capi = FunctionalCoreClientApiProxy.Create(events: events, world: world);

        var src = new LumonSceneTraceSceneChunkSnapshotSource(capi, versionProvider, lightIds);

        ChunkKey key = ChunkKey.FromChunkCoords(0, 0, 0);
        int version = versionProvider.GetCurrentVersion(key);

        using IChunkSnapshot? snapshot = await src.TryCreateSnapshotAsync(key, version, CancellationToken.None);
        var typed = Assert.IsType<PooledChunkSnapshot<LumonSceneTraceSceneSourceCell>>(snapshot);

        Assert.Equal(1, typed.Voxels.Span[0].IsSolid);
        Assert.Equal(1, typed.Voxels.Span[0].LightId);
        Assert.Equal(1, getLightRgbCalls);
    }

    private static class TestBlocks
    {
        public static readonly Block Air = new() { BlockId = 0 };
        public static readonly Block SolidFull = new() { BlockId = 1, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
    }

    private class TestWorldChunkProxy : DispatchProxy
    {
        private IChunkBlocks? blocks;
        private IChunkLight? lighting;
        private bool unpackResult;
        private bool disposed;

        public bool UnpackReadOnlyCalled { get; private set; }

        public static IWorldChunk Create(
            IChunkBlocks blocks,
            IChunkLight lighting,
            bool unpackResult,
            bool disposed,
            out TestWorldChunkProxy proxy)
        {
            object obj = Create<IWorldChunk, TestWorldChunkProxy>();
            proxy = (TestWorldChunkProxy)obj;
            proxy.blocks = blocks;
            proxy.lighting = lighting;
            proxy.unpackResult = unpackResult;
            proxy.disposed = disposed;
            return (IWorldChunk)obj;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;

            if (targetMethod.Name == "get_Disposed")
            {
                return disposed;
            }

            if (targetMethod.Name == "get_Data")
            {
                return blocks;
            }

            if (targetMethod.Name == "get_Lighting")
            {
                return lighting;
            }

            if (targetMethod.Name == nameof(IWorldChunk.Unpack_ReadOnly))
            {
                UnpackReadOnlyCalled = true;
                return unpackResult;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class TestChunkBlocksProxy : DispatchProxy
    {
        private int[]? data;

        public int TakeBulkReadLockCalls { get; private set; }
        public int ReleaseBulkReadLockCalls { get; private set; }

        public int[] Data => data ?? Array.Empty<int>();

        public static IChunkBlocks Create(int[] data, out TestChunkBlocksProxy proxy)
        {
            object obj = Create<IChunkBlocks, TestChunkBlocksProxy>();
            proxy = (TestChunkBlocksProxy)obj;
            proxy.data = data;
            return (IChunkBlocks)obj;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            string name = targetMethod.Name;

            if (name == nameof(IChunkBlocks.TakeBulkReadLock))
            {
                TakeBulkReadLockCalls++;
                return null;
            }

            if (name == nameof(IChunkBlocks.ReleaseBulkReadLock))
            {
                ReleaseBulkReadLockCalls++;
                return null;
            }

            if (name == nameof(IChunkBlocks.GetBlockIdUnsafe))
            {
                int i = (int)args[0]!;
                return data![i];
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class TestChunkLightProxy : DispatchProxy
    {
        private byte[]? blockLight;
        private byte[]? sunLight;

        public static IChunkLight Create(byte[] blockLight, byte[] sunLight, out TestChunkLightProxy proxy)
        {
            object obj = Create<IChunkLight, TestChunkLightProxy>();
            proxy = (TestChunkLightProxy)obj;
            proxy.blockLight = blockLight;
            proxy.sunLight = sunLight;
            return (IChunkLight)obj;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            string name = targetMethod.Name;
            if (name == nameof(IChunkLight.GetBlocklight))
            {
                int i = (int)args[0]!;
                return (int)blockLight![i];
            }

            if (name == nameof(IChunkLight.GetSunlight))
            {
                int i = (int)args[0]!;
                return (int)sunLight![i];
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class FunctionalBlockAccessorProxy : DispatchProxy
    {
        private System.Func<int, int, int, IWorldChunk?>? getChunk;
        private System.Func<int, int, int, int>? getLightRgb;

        public static IBlockAccessor Create(
            System.Func<int, int, int, IWorldChunk?> getChunk,
            System.Func<int, int, int, int> getLightRgb)
        {
            object proxy = Create<IBlockAccessor, FunctionalBlockAccessorProxy>();
            var typed = (FunctionalBlockAccessorProxy)proxy;
            typed.getChunk = getChunk;
            typed.getLightRgb = getLightRgb;
            return (IBlockAccessor)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            if (targetMethod.Name == nameof(IBlockAccessor.GetChunk))
            {
                return getChunk!((int)args[0]!, (int)args[1]!, (int)args[2]!);
            }

            if (targetMethod.Name == nameof(IBlockAccessor.GetLightRGBsAsInt))
            {
                return getLightRgb!((int)args[0]!, (int)args[1]!, (int)args[2]!);
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class FunctionalClientWorldAccessorProxy : DispatchProxy
    {
        private IBlockAccessor? blockAccessor;
        private System.Func<int, Block>? getBlockById;

        public static IClientWorldAccessor Create(IBlockAccessor blockAccessor, System.Func<int, Block> getBlockById)
        {
            object proxy = Create<IClientWorldAccessor, FunctionalClientWorldAccessorProxy>();
            var typed = (FunctionalClientWorldAccessorProxy)proxy;
            typed.blockAccessor = blockAccessor;
            typed.getBlockById = getBlockById;
            return (IClientWorldAccessor)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            if (targetMethod.Name == "get_BlockAccessor")
            {
                return blockAccessor;
            }

            if (targetMethod.Name == nameof(IWorldAccessor.GetBlock) && args.Length == 1 && args[0] is int id)
            {
                return getBlockById!(id);
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class FunctionalClientEventApiProxy : DispatchProxy
    {
        private bool runInline;

        public static IClientEventAPI Create(bool runMainThreadTaskInline)
        {
            object proxy = Create<IClientEventAPI, FunctionalClientEventApiProxy>();
            var typed = (FunctionalClientEventApiProxy)proxy;
            typed.runInline = runMainThreadTaskInline;
            return (IClientEventAPI)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            if (targetMethod.Name == nameof(IClientEventAPI.EnqueueMainThreadTask))
            {
                if (args.Length > 0 && args[0] is Action a)
                {
                    if (runInline)
                    {
                        a();
                    }

                    return null;
                }
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }

    private class FunctionalCoreClientApiProxy : DispatchProxy
    {
        private IClientEventAPI? events;
        private IClientWorldAccessor? world;

        public static ICoreClientAPI Create(IClientEventAPI events, IClientWorldAccessor world)
        {
            object proxy = Create<ICoreClientAPI, FunctionalCoreClientApiProxy>();
            var typed = (FunctionalCoreClientApiProxy)proxy;
            typed.events = events;
            typed.world = world;
            return (ICoreClientAPI)proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;

            if (targetMethod.Name == "get_Event")
            {
                return events;
            }

            if (targetMethod.Name == "get_World")
            {
                return world;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void)) return null;
            return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
        }
    }
}
