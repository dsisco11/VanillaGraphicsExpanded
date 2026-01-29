using System;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LumonScenePageRequestGpu
{
    public readonly uint ChunkSlot;
    public readonly uint VirtualPageIndex; // linear in [0..VirtualPagesPerChunk)
    public readonly uint Mip;
    public readonly uint Flags; // v1: used as packed flags; feedback gather encodes original patchId here for CPU scheduling/debug

    public LumonScenePageRequestGpu(uint chunkSlot, uint virtualPageIndex, uint mip, uint flags)
    {
        ChunkSlot = chunkSlot;
        VirtualPageIndex = virtualPageIndex;
        Mip = mip;
        Flags = flags;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LumonSceneCaptureWorkGpu
{
    public readonly uint PhysicalPageId;
    public readonly uint ChunkSlot;
    public readonly uint PatchId;
    public readonly uint VirtualPageIndex;

    public LumonSceneCaptureWorkGpu(uint physicalPageId, uint chunkSlot, uint patchId, uint virtualPageIndex)
    {
        PhysicalPageId = physicalPageId;
        ChunkSlot = chunkSlot;
        PatchId = patchId;
        VirtualPageIndex = virtualPageIndex;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct LumonSceneRelightWorkGpu
{
    public readonly uint PhysicalPageId;
    public readonly uint ChunkSlot;
    public readonly uint PatchId;
    public readonly uint VirtualPageIndex;

    public LumonSceneRelightWorkGpu(uint physicalPageId, uint chunkSlot, uint patchId, uint virtualPageIndex)
    {
        PhysicalPageId = physicalPageId;
        ChunkSlot = chunkSlot;
        PatchId = patchId;
        VirtualPageIndex = virtualPageIndex;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LumonSceneMeshCardCaptureWorkGpu
{
    public readonly uint PhysicalPageId;
    public readonly uint TriangleOffset;
    public readonly uint TriangleCount;
    public readonly uint Unused0;

    public LumonSceneMeshCardCaptureWorkGpu(uint physicalPageId, uint triangleOffset, uint triangleCount, uint unused0 = 0)
    {
        PhysicalPageId = physicalPageId;
        TriangleOffset = triangleOffset;
        TriangleCount = triangleCount;
        Unused0 = unused0;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LumonSceneMeshCardTriangleGpu
{
    public readonly System.Numerics.Vector4 P0;
    public readonly System.Numerics.Vector4 P1;
    public readonly System.Numerics.Vector4 P2;
    public readonly System.Numerics.Vector4 N0;

    public LumonSceneMeshCardTriangleGpu(
        System.Numerics.Vector4 p0,
        System.Numerics.Vector4 p1,
        System.Numerics.Vector4 p2,
        System.Numerics.Vector4 n0)
    {
        P0 = p0;
        P1 = p1;
        P2 = p2;
        N0 = n0;
    }
}

/// <summary>
/// GL 4.3 work queue: an appendable item buffer (SSBO) plus an atomic counter for write index.
/// </summary>
internal sealed class LumonSceneWorkQueueGpu<T> : IDisposable where T : unmanaged
{
    private readonly string debugName;
    private readonly int capacityItems;

    private GpuAtomicCounterBuffer? counter;
    private GpuShaderStorageBuffer? items;

    public int CapacityItems => capacityItems;

    public GpuAtomicCounterBuffer Counter => counter ?? throw new InvalidOperationException("GPU resources not created.");
    public GpuShaderStorageBuffer Items => items ?? throw new InvalidOperationException("GPU resources not created.");

    public LumonSceneWorkQueueGpu(string debugName, int capacityItems)
    {
        if (capacityItems <= 0) throw new ArgumentOutOfRangeException(nameof(capacityItems));
        this.debugName = debugName ?? throw new ArgumentNullException(nameof(debugName));
        this.capacityItems = capacityItems;
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureCreated()
    {
        if (counter is not null && items is not null)
        {
            return;
        }

        counter ??= GpuAtomicCounterBuffer.Create(BufferUsageHint.DynamicDraw, debugName: $"{debugName}.Counter(ACBO)");
        counter.InitializeCounters(counterCount: 1, initialValue: 0);

        items ??= GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw, debugName: $"{debugName}.Items(SSBO)");
        int bytes = checked(capacityItems * Unsafe.SizeOf<T>());
        items.EnsureCapacity(bytes, growExponentially: false);
    }

    /// <summary>
    /// Resets the append counter to 0.
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void Reset()
    {
        if (counter is null)
        {
            return;
        }

        Span<uint> zero = stackalloc uint[1] { 0u };
        counter.UploadSubData((ReadOnlySpan<uint>)zero, dstOffsetBytes: 0);
    }

    /// <summary>
    /// Convenience for CPU-produced work: resets counter to 0, uploads <paramref name="items"/> to the items SSBO,
    /// then sets the counter to the item count.
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void ResetAndUpload(ReadOnlySpan<T> itemsToUpload)
    {
        EnsureCreated();

        int count = Math.Min(itemsToUpload.Length, capacityItems);

        Reset();

        if (count > 0)
        {
            items!.UploadSubData(itemsToUpload[..count], dstOffsetBytes: 0);
        }

        Span<uint> c = stackalloc uint[1] { (uint)count };
        counter!.UploadSubData((ReadOnlySpan<uint>)c, dstOffsetBytes: 0);
    }

    public void Dispose()
    {
        items?.Dispose();
        items = null;

        counter?.Dispose();
        counter = null;
    }
}
