using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// GPU-side page table storage (v1: mip0 only) for a single field.
/// </summary>
internal sealed class LumonScenePageTableGpuResources : IDisposable
{
    public const PixelInternalFormat PageTableFormat = PixelInternalFormat.R32ui; // packed LumonScenePageTableEntry

    private readonly LumonSceneField field;
    private readonly int chunkSlotCount;

    private Texture3D? pageTableMip0;

    public LumonSceneField Field => @field;
    public int ChunkSlotCount => chunkSlotCount;

    public Texture3D PageTableMip0 => pageTableMip0 ?? throw new InvalidOperationException("GPU resources not created.");

    public LumonScenePageTableGpuResources(LumonSceneField field, int chunkSlotCount)
    {
        if (chunkSlotCount <= 0) throw new ArgumentOutOfRangeException(nameof(chunkSlotCount));

        this.field = field;
        this.chunkSlotCount = chunkSlotCount;
    }

    /// <summary>
    /// Must be called on the render thread (GL context required).
    /// </summary>
    public void EnsureCreated()
    {
        if (pageTableMip0 is not null)
        {
            return;
        }

        // NOTE: This is potentially large for the Far field: 128*128*chunkSlots*4 bytes.
        // The long-term plan is to reduce this via chunk-slot budgeting and/or a more sparse representation,
        // but v1 keeps a simple dense table for deterministic addressing.
        pageTableMip0 = Texture3D.Create(
            width: LumonSceneVirtualAtlasConstants.VirtualPageTableWidth,
            height: LumonSceneVirtualAtlasConstants.VirtualPageTableHeight,
            depth: chunkSlotCount,
            format: PageTableFormat,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture2DArray,
            debugName: $"LumOn.LumonScene.{field}.PageTableMip0(R32UI)");

        // Best-effort clear-to-zero so all entries start invalid.
        _ = pageTableMip0.TryClearToZero();
    }

    public void Dispose()
    {
        pageTableMip0?.Dispose();
        pageTableMip0 = null;
    }
}
