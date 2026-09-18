using System;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// GPU buffer for <see cref="LumonSceneChunkSlotInfoGpu"/> entries (SSBO in GL 4.3 path).
/// </summary>
internal sealed class LumonSceneChunkSlotInfoGpuBuffer : IDisposable
{
    private readonly LumonSceneField field;
    private readonly int capacityEntries;

    private GpuShaderStorageBuffer? ssbo;

    public LumonSceneField Field => @field;
    public int CapacityEntries => capacityEntries;
    public GpuShaderStorageBuffer Ssbo => ssbo ?? throw new InvalidOperationException("GPU resources not created.");

    public LumonSceneChunkSlotInfoGpuBuffer(LumonSceneField field, int capacityEntries)
    {
        if (capacityEntries <= 0) throw new ArgumentOutOfRangeException(nameof(capacityEntries));
        this.field = field;
        this.capacityEntries = capacityEntries;
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureCreated()
    {
        if (ssbo is not null)
        {
            return;
        }

        ssbo = GpuShaderStorageBuffer.Create(
            usage: OpenTK.Graphics.OpenGL.BufferUsageHint.DynamicDraw,
            debugName: $"LumOn.LumonScene.{field}.ChunkSlotInfo(SSBO)");

        int bytes = checked(capacityEntries * System.Runtime.InteropServices.Marshal.SizeOf<LumonSceneChunkSlotInfoGpu>());
        ssbo.EnsureCapacity(bytes, growExponentially: false);
    }

    public void Dispose()
    {
        ssbo?.Dispose();
        ssbo = null;
    }
}

