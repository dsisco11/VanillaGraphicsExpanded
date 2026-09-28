using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded;

/// <summary>Provides an unbiased receiver target without requiring engine SSAO or an additional MRT slot.</summary>
public sealed partial class GBufferManager
{
    private const int PositionSlotId = 3;
    private DynamicTexture2D? fallbackPosition;

    /// <summary>View-space position target; only negative-normal-alpha pixels require its contents.</summary>
    public int PositionTextureId { get; private set; }

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
            PositionTextureId = enginePosition;
            return;
        }

        // Do not insert this owned texture into the engine's deletion list. Its framebuffer
        // attachment is sufficient; VGE owns its lifetime alongside the other G-buffer textures.
        fallbackPosition ??= DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f,
            debugName: "gReceiverPosition");
        fallbackPosition.DisableMipmaps();
        fallbackPosition.SetTexFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        fallbackPosition.SetTexWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
        PositionTextureId = fallbackPosition.TextureId;
    }
    #endregion
}
