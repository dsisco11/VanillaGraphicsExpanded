using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumonTraceSceneRuntimeWiringEndToEndTests : RenderTestBase
{
    public LumonTraceSceneRuntimeWiringEndToEndTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public async Task SnapshotToChunkProcessingToGpuBuild_WritesOccupancyTexture()
    {
        EnsureContextValid();

        using var helper = CreateShaderHelperOrSkip();
        int program = CompileAndLinkCompute(helper, "lumonscene_trace_scene_region_to_clipmap.csh");

        // World-cell (aka chunk) payload: all solid except one air cell.
        const int len = LumonSceneTraceSceneRegionUploadGpuResources.RegionCellCount;
        int[] blocks = new int[len];
        Array.Fill(blocks, 1);
        blocks[1] = 0;

        byte[] blockLight = new byte[len];
        byte[] sunLight = new byte[len];

        IChunkBlocks chunkBlocks = TestChunkBlocksProxy.Create(blocks);
        IChunkLight chunkLight = TestChunkLightProxy.Create(blockLight, sunLight);
        IWorldChunk chunk = TestWorldChunkProxy.Create(chunkBlocks, chunkLight, unpackResult: true, disposed: false);

        var versionProvider = new LumonSceneTraceSceneChunkVersionProvider();
        var lightIds = new LumonSceneTraceSceneLightIdRegistry();

        var blockAccessor = FunctionalBlockAccessorProxy.Create(
            getChunk: (_, _, _) => chunk,
            getLightRgb: (_, _, _) => 0);

        var world = FunctionalClientWorldAccessorProxy.Create(
            blockAccessor: blockAccessor,
            getBlockById: id => id == 1 ? TestBlocks.SolidFull : TestBlocks.Air);

        var events = FunctionalClientEventApiProxy.Create(runMainThreadTaskInline: true);

        ICoreClientAPI capi = FunctionalCoreClientApiProxy.Create(events: events, world: world);

        var snapshotSource = new LumonSceneTraceSceneChunkSnapshotSource(capi, versionProvider, lightIds);
        using var chunkProcessing = new ChunkProcessingService(
            snapshotSource: snapshotSource,
            versionProvider: versionProvider,
            options: new ChunkProcessingServiceOptions { WorkerCount = 1 });

        ChunkKey key = ChunkKey.FromChunkCoords(0, 0, 0);
        int version = versionProvider.GetCurrentVersion(key);

        var processor = new LumonSceneTraceSceneRegionProcessor();
        ChunkWorkResult<LumonSceneTraceSceneRegionArtifact> res =
            await chunkProcessing.RequestAsync(key, version, processor, options: null, ct: CancellationToken.None);

        Assert.True(
            res.Status == ChunkWorkStatus.Success,
            $"Expected Success but got {res.Status} (Error={res.Error}, Reason='{res.Reason ?? "<null>"}').");
        Assert.NotNull(res.Artifact);

        // GPU clipmap build: scatter the region payload into the ring-buffered occupancy volume.
        using var resources = new LumonSceneOccupancyClipmapGpuResources(
            resolution: 64,
            levels: 1,
            debugNamePrefix: "Test.TraceScene");

        VectorInt3[] regionCoords = { new VectorInt3(0, 0, 0) };
        ReadOnlyMemory<uint>[] payloads = { res.Artifact!.PayloadWords };
        using var staging = new LumonSceneTraceSceneRegionUploadGpuResources(maxRegionUpdatesPerBatch: 16);
        int uploaded = staging.UploadBatch(regionCoords, payloads, levelMask: 1u, versionOrPad: 0u);
        Assert.Equal(1, uploaded);

        GL.UseProgram(program);
        staging.BindForCompute();

        resources.OccupancyLevels[0].BindImageUnit(
            unit: 0,
            access: TextureAccess.WriteOnly,
            level: 0,
            layered: true,
            layer: 0,
            format: SizedInternalFormat.R32ui);

        SetUniform1i(program, "vge_levels", 1);
        SetUniform1i(program, "vge_resolution", resources.Resolution);
        SetUniform1ui(program, "vge_regionUpdateCount", 1u);
        SetUniform3i(program, "vge_originMinCell[0]", 0, 0, 0);
        SetUniform3i(program, "vge_ring[0]", 0, 0, 0);

        GL.DispatchCompute(4, 4, 4);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        // Read back occupancy level 0 and validate a few positions.
        uint[] occ = ReadTexImageR32ui(resources.OccupancyLevels[0].TextureId, TextureTarget.Texture3D, 64, 64, 64);

        static int Linear64(int x, int y, int z) => (z * 64 + y) * 64 + x;

        uint expectedSolid = LumonSceneOccupancyPacking.Pack(0, 0, 0, 1);

        Assert.Equal(expectedSolid, occ[Linear64(0, 0, 0)]);
        Assert.Equal(0u, occ[Linear64(1, 0, 0)]); // air cell
        Assert.Equal(expectedSolid, occ[Linear64(31, 31, 31)]);
        Assert.Equal(0u, occ[Linear64(33, 0, 0)]); // outside the written 32^3 region

        GL.DeleteProgram(program);
    }

    private static uint[] ReadTexImageR32ui(int textureId, TextureTarget target, int width, int height, int depth)
    {
        GL.BindTexture(target, textureId);
        uint[] data = new uint[checked(width * height * depth)];
        GL.GetTexImage(target, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        GL.BindTexture(target, 0);
        return data;
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

        public static IWorldChunk Create(IChunkBlocks blocks, IChunkLight lighting, bool unpackResult, bool disposed)
        {
            object obj = Create<IWorldChunk, TestWorldChunkProxy>();
            var proxy = (TestWorldChunkProxy)obj;
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

        public static IChunkBlocks Create(int[] data)
        {
            object obj = Create<IChunkBlocks, TestChunkBlocksProxy>();
            var proxy = (TestChunkBlocksProxy)obj;
            proxy.data = data;
            return (IChunkBlocks)obj;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            if (targetMethod.Name == nameof(IChunkBlocks.GetBlockIdUnsafe))
            {
                int i = (int)args[0]!;
                return data![i];
            }

            if (targetMethod.Name == nameof(IChunkBlocks.TakeBulkReadLock) || targetMethod.Name == nameof(IChunkBlocks.ReleaseBulkReadLock))
            {
                return null;
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

        public static IChunkLight Create(byte[] blockLight, byte[] sunLight)
        {
            object obj = Create<IChunkLight, TestChunkLightProxy>();
            var proxy = (TestChunkLightProxy)obj;
            proxy.blockLight = blockLight;
            proxy.sunLight = sunLight;
            return (IChunkLight)obj;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null) return null;
            args ??= Array.Empty<object?>();

            if (targetMethod.Name == nameof(IChunkLight.GetBlocklight))
            {
                int i = (int)args[0]!;
                return (int)blockLight![i];
            }

            if (targetMethod.Name == nameof(IChunkLight.GetSunlight))
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

            if (targetMethod.Name == nameof(IClientEventAPI.EnqueueMainThreadTask) && args.Length > 0 && args[0] is Action a)
            {
                if (runInline)
                {
                    a();
                }

                return null;
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

            if (targetMethod.Name == "get_Side")
            {
                return EnumAppSide.Client;
            }

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

    private static ShaderTestHelper CreateShaderHelperOrSkip()
    {
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        if (!Directory.Exists(shaderPath) || !Directory.Exists(includePath))
        {
            Assert.Skip("Shader assets not available - test output content may be missing");
        }

        return new ShaderTestHelper(shaderPath, includePath);
    }

    private static int CompileAndLinkCompute(ShaderTestHelper helper, string computeShaderFile)
    {
        var cs = helper.CompileShader(computeShaderFile, ShaderType.ComputeShader);
        Assert.True(cs.IsSuccess, cs.ErrorMessage);

        int program = GL.CreateProgram();
        GL.AttachShader(program, cs.ShaderId);
        GL.LinkProgram(program);

        GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int ok);
        string log = GL.GetProgramInfoLog(program) ?? string.Empty;
        Assert.True(ok != 0, $"Compute program link failed:\n{log}");

        return program;
    }

    private static void SetUniform1i(int program, string name, int value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void SetUniform1ui(int program, string name, uint value)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform1(loc, value);
    }

    private static void SetUniform3i(int program, string name, int x, int y, int z)
    {
        int loc = GL.GetUniformLocation(program, name);
        Assert.True(loc >= 0, $"Missing uniform {name}");
        GL.Uniform3(loc, x, y, z);
    }
}
