using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Tracks pixel-pack layout used by framebuffer readback.</summary>
internal sealed partial class GlStateCache
{
    private PixelPackState? pixelPackState;

    #region Pixel pack state
    /// <summary>Forgets pack state after external GL changes or a context change.</summary>
    public void DirtyPixelPackState() => pixelPackState = null;

    /// <summary>Gets the cached layout, querying GL only when the state is unknown.</summary>
    public PixelPackState GetPixelPackState()
    {
        // External rendering boundaries must invalidate the cache before borrowing state.
        return pixelPackState ??= new PixelPackState(
            GL.GetInteger(GetPName.PackAlignment),
            GL.GetInteger(GetPName.PackRowLength),
            GL.GetInteger(GetPName.PackSkipRows),
            GL.GetInteger(GetPName.PackSkipPixels),
            GL.GetInteger(GetPName.PackSwapBytes) != 0);
    }

    /// <summary>Applies a pack layout, omitting driver calls for known unchanged values.</summary>
    public void SetPixelPackState(PixelPackState state)
    {
        if (state.Alignment is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(state));
        if (state.RowLength < 0 || state.SkipRows < 0 || state.SkipPixels < 0) throw new ArgumentOutOfRangeException(nameof(state));
        var previous = pixelPackState;
        // Leave the cache unknown if a driver call throws partway through the update.
        pixelPackState = null;
        if (previous?.Alignment != state.Alignment) GL.PixelStore(PixelStoreParameter.PackAlignment, state.Alignment);
        if (previous?.RowLength != state.RowLength) GL.PixelStore(PixelStoreParameter.PackRowLength, state.RowLength);
        if (previous?.SkipRows != state.SkipRows) GL.PixelStore(PixelStoreParameter.PackSkipRows, state.SkipRows);
        if (previous?.SkipPixels != state.SkipPixels) GL.PixelStore(PixelStoreParameter.PackSkipPixels, state.SkipPixels);
        if (previous?.SwapBytes != state.SwapBytes) GL.PixelStore(PixelStoreParameter.PackSwapBytes, state.SwapBytes ? 1 : 0);
        pixelPackState = state;
    }

    /// <summary>Applies a temporary layout and restores the previous layout on disposal.</summary>
    public PixelPackScope SetPixelPackScope(PixelPackState state)
    {
        var previous = GetPixelPackState();
        SetPixelPackState(state);
        return new PixelPackScope(this, previous);
    }

    /// <summary>Describes row layout and byte ordering for non-bitmap framebuffer readback.</summary>
    public readonly record struct PixelPackState(int Alignment, int RowLength = 0, int SkipRows = 0, int SkipPixels = 0, bool SwapBytes = false);

    /// <summary>Restores a borrowed pack layout through the owning cache.</summary>
    public readonly struct PixelPackScope : IDisposable
    {
        private readonly GlStateCache cache;
        private readonly PixelPackState previous;

        /// <summary>Records the layout to restore when this scope ends.</summary>
        internal PixelPackScope(GlStateCache cache, PixelPackState previous)
        {
            this.cache = cache;
            this.previous = previous;
        }

        /// <summary>Restores the layout, updating the cache along with GL.</summary>
        public void Dispose() => cache.SetPixelPackState(previous);
    }
    #endregion
}
