using System;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.2: GPU staging resources for region-driven TraceScene clipmap updates (GL 4.3).
/// </summary>
internal sealed class LumonSceneTraceSceneRegionUploadGpuResources : IDisposable
{
    public const int RegionCellCount = LumonSceneTraceSceneClipmapMath.RegionSize
                                      * LumonSceneTraceSceneClipmapMath.RegionSize
                                      * LumonSceneTraceSceneClipmapMath.RegionSize; // 32^3

    // Work item uploaded by CPU; consumed by compute to scatter a region payload into one or more clipmap levels.
    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct RegionUpdateGpu
    {
        // Region coordinate in 32^3 units (stored as ivec4-compatible 16B lane; W is padding).
        public readonly VectorInt4 RegionCoord;

        // Offset (in uint words) into the payload staging buffer for this region.
        public readonly uint SrcOffsetWords;

        // Bitmask selecting which clipmap levels to update (bit i => level i).
        public readonly uint LevelMask;

        // Version/pad (optional for debug/stale rejection); keep struct 32B aligned.
        public readonly uint VersionOrPad;

        // Explicit padding to ensure std430 array stride is 32B (vec4 + 4 uints).
        public readonly uint Padding0;

        public RegionUpdateGpu(in VectorInt3 regionCoord, uint srcOffsetWords, uint levelMask, uint versionOrPad = 0)
        {
            RegionCoord = new VectorInt4(regionCoord.X, regionCoord.Y, regionCoord.Z, 0);
            SrcOffsetWords = srcOffsetWords;
            LevelMask = levelMask;
            VersionOrPad = versionOrPad;
            Padding0 = 0;
        }
    }

    private readonly int maxRegionUpdatesPerBatch;

    private GpuShaderStorageBuffer? payloadWordsSsbo;
    private int payloadWordsCapacity;

    private LumonSceneWorkQueueGpu<RegionUpdateGpu>? updates;

    public int MaxRegionUpdatesPerBatch => maxRegionUpdatesPerBatch;

    public GpuShaderStorageBuffer PayloadWordsSsbo => payloadWordsSsbo ?? throw new InvalidOperationException("GPU resources not created.");
    public LumonSceneWorkQueueGpu<RegionUpdateGpu> Updates => updates ?? throw new InvalidOperationException("GPU resources not created.");

    public LumonSceneTraceSceneRegionUploadGpuResources(int maxRegionUpdatesPerBatch = 16)
    {
        this.maxRegionUpdatesPerBatch = Math.Clamp(maxRegionUpdatesPerBatch, 1, 1024);
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureCreated()
    {
        payloadWordsSsbo ??= GpuShaderStorageBuffer.Create(
            BufferUsageHint.DynamicDraw,
            debugName: "LumOn.TraceScene.RegionPayloadWords(SSBO)");

        updates ??= new LumonSceneWorkQueueGpu<RegionUpdateGpu>(
            debugName: "LumOn.TraceScene.RegionUpdates",
            capacityItems: maxRegionUpdatesPerBatch);
        updates.EnsureCreated();
    }

    public void Dispose()
    {
        updates?.Dispose();
        updates = null;

        payloadWordsSsbo?.Dispose();
        payloadWordsSsbo = null;
        payloadWordsCapacity = 0;
    }

    /// <summary>
    /// Ensures the payload staging SSBO can hold <paramref name="requiredWords"/> 32-bit words.
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsurePayloadCapacityWords(int requiredWords)
    {
        EnsureCreated();

        requiredWords = Math.Max(0, requiredWords);
        if (payloadWordsCapacity >= requiredWords)
        {
            return;
        }

        int requiredBytes = checked(requiredWords * sizeof(uint));
        payloadWordsSsbo!.EnsureCapacity(requiredBytes, growExponentially: true);
        payloadWordsCapacity = payloadWordsSsbo!.SizeBytes / sizeof(uint);
    }

    /// <summary>
    /// Uploads a packed region payload into the staging SSBO at the given word offset.
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void UploadPayloadWords(ReadOnlySpan<uint> payloadWords, int dstOffsetWords)
    {
        EnsureCreated();

        if (dstOffsetWords < 0) throw new ArgumentOutOfRangeException(nameof(dstOffsetWords));

        int bytes = checked(payloadWords.Length * sizeof(uint));
        int dstOffsetBytes = checked(dstOffsetWords * sizeof(uint));
        payloadWordsSsbo!.UploadSubData(payloadWords, dstOffsetBytes: dstOffsetBytes, byteCount: bytes);
    }

    /// <summary>
    /// Convenience: batches <paramref name="regionPayloads"/> into the payload SSBO and uploads corresponding work items.
    /// Returns the number of uploaded region updates (clamped to batch capacity).
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public int UploadBatch(
        ReadOnlySpan<VectorInt3> regionCoords,
        ReadOnlySpan<ReadOnlyMemory<uint>> regionPayloads,
        uint levelMask,
        uint versionOrPad = 0)
    {
        EnsureCreated();

        int count = Math.Min(regionCoords.Length, regionPayloads.Length);
        count = Math.Min(count, maxRegionUpdatesPerBatch);
        if (count <= 0)
        {
            updates!.Reset();
            return 0;
        }

        // Allocate a contiguous staging layout:
        // payload[0] at offset 0, payload[1] at offset RegionCellCount, etc.
        int requiredWords = checked(count * RegionCellCount);
        EnsurePayloadCapacityWords(requiredWords);

        RegionUpdateGpu[] work = ArrayPool<RegionUpdateGpu>.Shared.Rent(count);
        try
        {
            for (int i = 0; i < count; i++)
            {
                int dstOffsetWords = checked(i * RegionCellCount);

                ReadOnlySpan<uint> src = regionPayloads[i].Span;
                if (src.Length < RegionCellCount)
                {
                    throw new ArgumentException($"Region payload {i} is too small (got {src.Length}, expected {RegionCellCount}).", nameof(regionPayloads));
                }

                UploadPayloadWords(src[..RegionCellCount], dstOffsetWords);
                work[i] = new RegionUpdateGpu(regionCoords[i], srcOffsetWords: (uint)dstOffsetWords, levelMask: levelMask, versionOrPad: versionOrPad);
            }

            updates!.ResetAndUpload(work.AsSpan(0, count));
            return count;
        }
        finally
        {
            ArrayPool<RegionUpdateGpu>.Shared.Return(work, clearArray: false);
        }
    }

    /// <summary>
    /// Binds the staging resources to the expected SSBO binding points for the future compute shader.
    /// </summary>
    /// <remarks>
    /// Proposed bindings for `lumonscene_trace_scene_region_to_clipmap.csh`:
    /// - binding=0: payload words SSBO (uint[])
    /// - binding=1: region updates SSBO (RegionUpdateGpu[])
    /// - atomic counter (Updates.Counter) can be used as updateCount if desired
    /// </remarks>
    public void BindForCompute()
    {
        EnsureCreated();

        payloadWordsSsbo!.BindBase(bindingIndex: 0);
        updates!.Items.BindBase(bindingIndex: 1);
        updates.Counter.BindBase(bindingIndex: 0);
    }
}
