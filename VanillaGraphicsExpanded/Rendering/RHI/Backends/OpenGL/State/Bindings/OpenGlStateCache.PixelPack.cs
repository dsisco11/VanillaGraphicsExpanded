using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains independently established pixel-pack fields in the existing context cache.</summary>
internal sealed partial class OpenGlStateCache
{
    private PixelPackState pixelPack;
    private byte pixelPackKnown;

    #region Internal API
    /// <summary>Gets a complete layout only when every field has been observed or acquired.</summary>
    internal PixelPackState? PixelPackKnowledge
    {
        get => pixelPackKnown == byte.MaxValue ? pixelPack : null;
        set { pixelPack = value.GetValueOrDefault(); pixelPackKnown = value.HasValue ? byte.MaxValue : (byte)0; }
    }
    /// <summary>Withdraws layout knowledge after context loss or an uncertain failed transition.</summary>
    internal void DirtyPixelPackState() => pixelPackKnown = 0;

    /// <summary>Reads an established field without querying unrelated missing layout fields.</summary>
    internal bool TryGetPixelPackField(PixelStoreParameter parameter, out int value)
    {
        switch (parameter)
        {
            case PixelStoreParameter.PackAlignment: value = pixelPack.Alignment; return (pixelPackKnown & 1) != 0;
            case PixelStoreParameter.PackRowLength: value = pixelPack.RowLength; return (pixelPackKnown & 2) != 0;
            case PixelStoreParameter.PackSkipRows: value = pixelPack.SkipRows; return (pixelPackKnown & 4) != 0;
            case PixelStoreParameter.PackSkipPixels: value = pixelPack.SkipPixels; return (pixelPackKnown & 8) != 0;
            case PixelStoreParameter.PackSwapBytes: value = (pixelPack.SwapBytes ? 1 : 0); return (pixelPackKnown & 16) != 0;
            case PixelStoreParameter.PackLsbFirst: value = (pixelPack.LsbFirst ? 1 : 0); return (pixelPackKnown & 32) != 0;
            case PixelStoreParameter.PackImageHeight: value = pixelPack.ImageHeight; return (pixelPackKnown & 64) != 0;
            case PixelStoreParameter.PackSkipImages: value = pixelPack.SkipImages; return (pixelPackKnown & 128) != 0;
            default: value = 0; return false;
        }
    }

    /// <summary>Commits one validated native command effect without manufacturing unknown sibling fields.</summary>
    internal void ObservePixelPackField(PixelStoreParameter parameter, int value)
    {
        switch (parameter)
        {
            case PixelStoreParameter.PackAlignment: pixelPack = pixelPack with { Alignment = value }; pixelPackKnown |= 1; break;
            case PixelStoreParameter.PackRowLength: pixelPack = pixelPack with { RowLength = value }; pixelPackKnown |= 2; break;
            case PixelStoreParameter.PackSkipRows: pixelPack = pixelPack with { SkipRows = value }; pixelPackKnown |= 4; break;
            case PixelStoreParameter.PackSkipPixels: pixelPack = pixelPack with { SkipPixels = value }; pixelPackKnown |= 8; break;
            case PixelStoreParameter.PackSwapBytes: pixelPack = pixelPack with { SwapBytes = value != 0 }; pixelPackKnown |= 16; break;
            case PixelStoreParameter.PackLsbFirst: pixelPack = pixelPack with { LsbFirst = value != 0 }; pixelPackKnown |= 32; break;
            case PixelStoreParameter.PackImageHeight: pixelPack = pixelPack with { ImageHeight = value }; pixelPackKnown |= 64; break;
            case PixelStoreParameter.PackSkipImages: pixelPack = pixelPack with { SkipImages = value }; pixelPackKnown |= 128; break;
        }
    }
    /// <summary>Withdraws one field when its command outcome cannot be established from supplied arguments.</summary>
    internal void ForgetPixelPackField(PixelStoreParameter parameter)
    {
        switch (parameter)
        {
            case PixelStoreParameter.PackAlignment: pixelPackKnown &= 254; break;
            case PixelStoreParameter.PackRowLength: pixelPackKnown &= 253; break;
            case PixelStoreParameter.PackSkipRows: pixelPackKnown &= 251; break;
            case PixelStoreParameter.PackSkipPixels: pixelPackKnown &= 247; break;
            case PixelStoreParameter.PackSwapBytes: pixelPackKnown &= 239; break;
            case PixelStoreParameter.PackLsbFirst: pixelPackKnown &= 223; break;
            case PixelStoreParameter.PackImageHeight: pixelPackKnown &= 191; break;
            case PixelStoreParameter.PackSkipImages: pixelPackKnown &= 127; break;
        }
    }
    /// <summary>Describes row/image strides, skips and byte order.</summary>
    public readonly record struct PixelPackState(int Alignment, int RowLength = 0, int SkipRows = 0, int SkipPixels = 0, bool SwapBytes = false, bool LsbFirst = false, int ImageHeight = 0, int SkipImages = 0);
    #endregion
}
