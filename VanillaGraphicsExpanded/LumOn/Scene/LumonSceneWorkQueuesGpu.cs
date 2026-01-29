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
    public readonly uint Flags;

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

    public void Dispose()
    {
        items?.Dispose();
        items = null;

        counter?.Dispose();
        counter = null;
    }
}
