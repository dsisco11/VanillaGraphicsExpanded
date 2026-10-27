using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded;

/// <summary>Provides an unbiased receiver target without requiring engine SSAO or an additional MRT slot.</summary>
public sealed partial class GBufferManager
{
    private const int PositionSlotId = 3;
    private DynamicTexture2D? fallbackPosition;
    private BorrowedTexture? borrowedPosition;

    /// <summary>View-space position target; only negative-normal-alpha pixels require its contents.</summary>
    public int PositionTextureId { get; private set; }

    /// <summary>Lends the selected engine or owned receiver image through its original managed association.</summary>
    public GpuTextureRef PositionTexture => fallbackPosition is not null ? (GpuTextureRef)fallbackPosition : borrowedPosition;

    #region Receiver storage
    /// <summary>Borrows the engine position texture, allocating owned storage only when the engine omitted it.</summary>
    private void PrepareReceiverPosition(FrameBufferRef primary, int width, int height)
    {
        int enginePosition = primary.ColorTextureIds.Length > PositionSlotId
            ? primary.ColorTextureIds[PositionSlotId] : 0;
        if (enginePosition != 0)
        {
            fallbackPosition?.Dispose();
            fallbackPosition = null;
            BorrowedTexture.GetOrReplace(ref borrowedPosition, enginePosition);
            PositionTextureId = enginePosition;
            return;
        }

        borrowedPosition?.Dispose();
        borrowedPosition = null;
        // Do not insert this owned texture into the engine's deletion list. Its framebuffer
        // attachment is sufficient; VGE owns its lifetime alongside the other G-buffer textures.
        fallbackPosition ??= RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "gReceiverPosition" }).RequireResource();
        fallbackPosition.DisableMipmaps();
        fallbackPosition.SetTexFilter(SamplerFilter.Nearest, SamplerFilter.Nearest);
        fallbackPosition.SetTexWrap(SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
        PositionTextureId = fallbackPosition.TextureId;
    }
    #endregion
}
