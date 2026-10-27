namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns executed pixel-unpack layout knowledge independently of native execution.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Retains the current certified layout; null means external state must be acquired.</summary>
    internal PixelUnpackState? PixelUnpackKnowledge { get; set; }
    /// <summary>Withdraws layout knowledge after unobserved native changes.</summary>
    internal void DirtyPixelUnpackState() => PixelUnpackKnowledge = null;
    /// <summary>Describes row/image strides, skips and byte order.</summary>
    public readonly record struct PixelUnpackState(int Alignment, int RowLength = 0, int SkipRows = 0, int SkipPixels = 0, bool SwapBytes = false, bool LsbFirst = false, int ImageHeight = 0, int SkipImages = 0);
    #endregion
}
