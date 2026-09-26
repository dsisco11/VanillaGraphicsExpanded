using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Captures and seeds the six faces of a small room using borrowed production geometry and owned cache storage.</summary>
internal sealed class SurfaceLightingScreenTraceFixture : IDisposable
{
    private const int Edge = 8, Tiles = 8;
    private readonly TraceGeometryGpuScene geometry;
    private readonly BinaryShaderApiFixture assets = new();
    private readonly SurfaceAtlasTextures atlas = new(64, 64, 1, "Tests.ScreenTraceCache");
    private readonly LumonScenePageTableGpuResources pageStorage;
    private readonly GpuShaderStorageBuffer work, metadata, slots, readiness;
    private readonly SurfaceLightingDispatch producer;
    private readonly LumonSceneCaptureWorkGpu[] capture;
    private readonly LumonSceneRelightWorkGpu[] lighting;
    private readonly VectorInt3 origin, dimensions;
    private int generation;

    /// <summary>Exposes only fixture-owned resources; consumers borrow this snapshot for one dispatch.</summary>
    public SurfaceLightingSnapshot Snapshot => new(atlas.Outgoing[generation & 1], atlas.Direct, atlas.Indirect,
        pageStorage.PageTableMip0, atlas.Material, metadata, slots, readiness, origin, dimensions, default,
        Edge, Tiles, Tiles * Tiles, generation);

    #region Construction and lifetime
    /// <summary>Allocates bounded patch descriptors for the original room bounds without changing its material registry or geometry.</summary>
    public SurfaceLightingScreenTraceFixture(TraceGeometryGpuScene geometry, VectorInt3 min, VectorInt3 max)
    {
        this.geometry = geometry;
        origin = new(min.X >> 5, min.Y >> 5, min.Z >> 5);
        dimensions = new((max.X >> 5) - origin.X + 1, (max.Y >> 5) - origin.Y + 1, (max.Z >> 5) - origin.Z + 1);
        int count = dimensions.X * dimensions.Y * dimensions.Z;
        pageStorage = new(LumonSceneField.Near, count);
        pageStorage.EnsureCreated();
        var entries = new uint[count << 14];
        var slotData = new int[count << 2];
        for (int z = 0; z < dimensions.Z; z++)
        for (int y = 0; y < dimensions.Y; y++)
        for (int x = 0; x < dimensions.X; x++)
        {
            int slot = x + dimensions.X * (z + dimensions.Z * y);
            slotData[slot << 2] = (origin.X + x) << 5;
            slotData[(slot << 2) + 1] = (origin.Y + y) << 5;
            slotData[(slot << 2) + 2] = (origin.Z + z) << 5;
        }
        var items = new List<LumonSceneCaptureWorkGpu>();
        var seen = new HashSet<(uint Slot, uint Patch)>();
        // Each solid boundary contributes its inward-facing patch, including patches crossing chunk boundaries.
        for (uint axis = 0; axis < 6; axis++)
        for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int x = min.X; x <= max.X; x++)
        {
            bool face = axis switch { 0 => x == min.X, 1 => x == max.X, 2 => y == min.Y,
                3 => y == max.Y, 4 => z == min.Z, _ => z == max.Z };
            if (!face) continue;
            int px = x & 31, py = y & 31, pz = z & 31;
            int plane = axis < 2 ? px : axis < 4 ? py : pz;
            int u = axis < 2 ? pz : px, v = axis < 2 ? py : axis < 4 ? pz : py;
            uint slot = (uint)((x >> 5) - origin.X + dimensions.X * ((z >> 5) - origin.Z + dimensions.Z * ((y >> 5) - origin.Y)));
            uint patch = 1 + 6 * (uint)((plane << 6) + ((v >> 2) << 3) + (u >> 2)) + axis;
            if (!seen.Add((slot, patch))) continue;
            uint page = (uint)items.Count + 1;
            items.Add(new(page, slot, patch, patch));
            entries[(slot << 14) + patch] = LumonScenePageTableEntryPacking.Pack(page, LumonScenePageTableEntryPacking.Flags.Resident).Packed;
        }
        Assert.InRange(items.Count, 1, Tiles * Tiles);
        capture = items.ToArray();
        lighting = capture.Select(item => new LumonSceneRelightWorkGpu(item.PhysicalPageId, item.ChunkSlot, 0, item.VirtualPageIndex)).ToArray();
        work = Buffer<LumonSceneCaptureWorkGpu>(capture);
        metadata = Buffer<LumonScenePatchMetadataGpu>(new LumonScenePatchMetadataGpu[capture.Length + 1]);
        slots = Buffer<int>(slotData);
        readiness = Buffer<uint>(new uint[capture.Length + 1]);
        pageStorage.PageTableMip0.UploadDataImmediate(entries, 0, 0, 0, 128, 128, count);
        producer = new(assets.Api);
    }

    /// <summary>Creates initialized typed storage through the GPU ownership abstraction.</summary>
    private static GpuShaderStorageBuffer Buffer<T>(ReadOnlySpan<T> values) where T : unmanaged
    {
        var buffer = GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw);
        int bytes = values.Length * Marshal.SizeOf<T>();
        buffer.EnsureCapacity(bytes, growExponentially: false);
        buffer.UploadSubData(values, 0, bytes);
        return buffer;
    }

    /// <summary>Releases only owned storage, leaving borrowed geometry and material registry lifetimes unchanged.</summary>
    public void Dispose()
    {
        producer.Dispose(); work.Dispose(); metadata.Dispose(); slots.Dispose(); readiness.Dispose();
        pageStorage.Dispose(); atlas.Dispose(); assets.Dispose();
    }
    #endregion

    #region Capture and lighting publication
    /// <summary>Runs actual capture and direct/emission seeding, exposing lighting only after every page completed.</summary>
    public bool CaptureAndSeed()
    {
        readiness.UploadSubData<uint>(new uint[capture.Length + 1], 0, (capture.Length + 1) << 2);
        work.UploadSubData<LumonSceneCaptureWorkGpu>(capture, 0, capture.Length << 4);
        Assert.True(LumonSceneCaptureVoxelComputeShader.TryCreate(assets.Api, out var shader, out string log), log);
        using (shader)
        using (shader!.UseScope())
        {
            shader.BindSharedGeometry(geometry); shader.BindCaptureWorkSsbo(work);
            shader.BindPatchMetaSsbo(metadata); shader.BindChunkSlotInfoSsbo(slots);
            shader.BindDepthAtlasImage(atlas.Depth); shader.BindMaterialAtlasImage(atlas.Material);
            shader.SetAtlasLayout(Edge, Tiles, Tiles * Tiles, 0);
            shader.Dispatch(1, 1, capture.Length);
            GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);
        }
        using (var result = work.MapRange<LumonSceneCaptureWorkGpu>(0, capture.Length, MapBufferAccessMask.MapReadBit))
        {
            Assert.True(result.IsMapped);
            foreach (var item in result.Span)
                if ((item.VirtualPageIndex & 0x80000000u) != 0) return false;
        }
        foreach (uint operation in new uint[] { 3, 0, 2 })
        {
            work.UploadSubData<LumonSceneRelightWorkGpu>(lighting, 0, lighting.Length << 4);
            producer.Run(geometry, Snapshot, atlas.Outgoing[1 - (generation & 1)], work, lighting.Length,
                operation, 64, 1, 256, (uint)generation, emission: true);
            using var result = work.MapRange<LumonSceneRelightWorkGpu>(0, lighting.Length, MapBufferAccessMask.MapReadBit);
            Assert.True(result.IsMapped);
            // Material capture can succeed for unsupported shapes; their lighting seed must still remain unavailable.
            foreach (var item in result.Span)
                if ((item.VirtualPageIndex & 0x80000000u) != 0) return false;
        }
        generation++;
        readiness.UploadSubData<uint>(Enumerable.Repeat(1u, capture.Length + 1).ToArray(), 0, (capture.Length + 1) << 2);
        return true;
    }
    #endregion
}
