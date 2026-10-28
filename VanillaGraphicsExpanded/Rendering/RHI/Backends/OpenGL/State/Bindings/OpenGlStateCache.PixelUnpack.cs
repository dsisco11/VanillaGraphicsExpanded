using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Retains independently established pixel-unpack fields in the existing context cache.</summary>
internal sealed partial class OpenGlStateCache
{
    private PixelUnpackState pixelUnpack;
    private byte pixelUnpackKnown;

    #region Internal API
    /// <summary>Gets a complete layout only when every field has been observed or acquired.</summary>
    internal PixelUnpackState? PixelUnpackKnowledge
    {
        get => pixelUnpackKnown == byte.MaxValue ? pixelUnpack : null;
        set { pixelUnpack = value.GetValueOrDefault(); pixelUnpackKnown = value.HasValue ? byte.MaxValue : (byte)0; }
    }
    /// <summary>Withdraws layout knowledge after context loss or an uncertain failed transition.</summary>
    internal void DirtyPixelUnpackState() => pixelUnpackKnown = 0;

    /// <summary>Reads an established field without querying unrelated missing layout fields.</summary>
    internal bool TryGetPixelUnpackField(PixelStoreParameter parameter, out int value)
    {
        switch (parameter)
        {
            case PixelStoreParameter.UnpackAlignment: value = pixelUnpack.Alignment; return (pixelUnpackKnown & 1) != 0;
            case PixelStoreParameter.UnpackRowLength: value = pixelUnpack.RowLength; return (pixelUnpackKnown & 2) != 0;
            case PixelStoreParameter.UnpackSkipRows: value = pixelUnpack.SkipRows; return (pixelUnpackKnown & 4) != 0;
            case PixelStoreParameter.UnpackSkipPixels: value = pixelUnpack.SkipPixels; return (pixelUnpackKnown & 8) != 0;
            case PixelStoreParameter.UnpackSwapBytes: value = (pixelUnpack.SwapBytes ? 1 : 0); return (pixelUnpackKnown & 16) != 0;
            case PixelStoreParameter.UnpackLsbFirst: value = (pixelUnpack.LsbFirst ? 1 : 0); return (pixelUnpackKnown & 32) != 0;
            case PixelStoreParameter.UnpackImageHeight: value = pixelUnpack.ImageHeight; return (pixelUnpackKnown & 64) != 0;
            case PixelStoreParameter.UnpackSkipImages: value = pixelUnpack.SkipImages; return (pixelUnpackKnown & 128) != 0;
            default: value = 0; return false;
        }
    }

    /// <summary>Commits one validated native command effect without manufacturing unknown sibling fields.</summary>
    internal void ObservePixelUnpackField(PixelStoreParameter parameter, int value)
    {
        switch (parameter)
        {
            case PixelStoreParameter.UnpackAlignment: pixelUnpack = pixelUnpack with { Alignment = value }; pixelUnpackKnown |= 1; break;
            case PixelStoreParameter.UnpackRowLength: pixelUnpack = pixelUnpack with { RowLength = value }; pixelUnpackKnown |= 2; break;
            case PixelStoreParameter.UnpackSkipRows: pixelUnpack = pixelUnpack with { SkipRows = value }; pixelUnpackKnown |= 4; break;
            case PixelStoreParameter.UnpackSkipPixels: pixelUnpack = pixelUnpack with { SkipPixels = value }; pixelUnpackKnown |= 8; break;
            case PixelStoreParameter.UnpackSwapBytes: pixelUnpack = pixelUnpack with { SwapBytes = value != 0 }; pixelUnpackKnown |= 16; break;
            case PixelStoreParameter.UnpackLsbFirst: pixelUnpack = pixelUnpack with { LsbFirst = value != 0 }; pixelUnpackKnown |= 32; break;
            case PixelStoreParameter.UnpackImageHeight: pixelUnpack = pixelUnpack with { ImageHeight = value }; pixelUnpackKnown |= 64; break;
            case PixelStoreParameter.UnpackSkipImages: pixelUnpack = pixelUnpack with { SkipImages = value }; pixelUnpackKnown |= 128; break;
        }
    }
    /// <summary>Withdraws one field when its command outcome cannot be established from supplied arguments.</summary>
    internal void ForgetPixelUnpackField(PixelStoreParameter parameter)
    {
        switch (parameter)
        {
            case PixelStoreParameter.UnpackAlignment: pixelUnpackKnown &= 254; break;
            case PixelStoreParameter.UnpackRowLength: pixelUnpackKnown &= 253; break;
            case PixelStoreParameter.UnpackSkipRows: pixelUnpackKnown &= 251; break;
            case PixelStoreParameter.UnpackSkipPixels: pixelUnpackKnown &= 247; break;
            case PixelStoreParameter.UnpackSwapBytes: pixelUnpackKnown &= 239; break;
            case PixelStoreParameter.UnpackLsbFirst: pixelUnpackKnown &= 223; break;
            case PixelStoreParameter.UnpackImageHeight: pixelUnpackKnown &= 191; break;
            case PixelStoreParameter.UnpackSkipImages: pixelUnpackKnown &= 127; break;
        }
    }
    /// <summary>Describes row/image strides, skips and byte order.</summary>
    public readonly record struct PixelUnpackState(int Alignment, int RowLength = 0, int SkipRows = 0, int SkipPixels = 0, bool SwapBytes = false, bool LsbFirst = false, int ImageHeight = 0, int SkipImages = 0);
    #endregion
}
