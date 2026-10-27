using VanillaGraphicsExpanded.Rendering.RHI;
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
        pageTableMip0 = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Format((OpenTK.Graphics.OpenGL.PixelInternalFormat)(PageTableFormat)), Width = System.Math.Max(1, LumonSceneVirtualAtlasConstants.VirtualPageTableWidth), Height = System.Math.Max(1, LumonSceneVirtualAtlasConstants.VirtualPageTableHeight), Depth = System.Math.Max(1, chunkSlotCount), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = $"LumOn.LumonScene.{field}.PageTableMip0(R32UI)" }).RequireResource();

        // Best-effort clear-to-zero so all entries start invalid.
        pageTableMip0.ClearToZero();
    }

    public void Dispose()
    {
        pageTableMip0?.Dispose();
        pageTableMip0 = null;
    }
}
