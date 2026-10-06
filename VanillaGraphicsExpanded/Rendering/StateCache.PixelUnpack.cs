using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Tracks pixel-unpack layout used by texture and bitmap uploads.</summary>
internal sealed partial class StateCache
{
    private PixelUnpackState? pixelUnpackState;

    #region Public API
    /// <summary>Forgets unpack state after explicitly reported external GL changes.</summary>
    public void DirtyPixelUnpackState() => pixelUnpackState = null;

    /// <summary>Gets the cached layout, querying GL only when the state is unknown.</summary>
    public PixelUnpackState GetPixelUnpackState()
    {
        // External rendering boundaries must invalidate the cache before borrowing state.
        return pixelUnpackState ??= new PixelUnpackState(
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackAlignment)),
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackRowLength)),
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackSkipRows)),
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackSkipPixels)),
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackSwapBytes)) != 0,
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackLsbFirst)) != 0,
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackImageHeight)),
            QueryBoundary(() => GL.GetInteger(GetPName.UnpackSkipImages)));
    }

    /// <summary>Applies an unpack layout, omitting driver calls for known unchanged values.</summary>
    public void SetPixelUnpackState(PixelUnpackState state)
    {
        if (state.Alignment is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(state));
        if (state.RowLength < 0 || state.SkipRows < 0 || state.SkipPixels < 0 || state.ImageHeight < 0 || state.SkipImages < 0) throw new ArgumentOutOfRangeException(nameof(state));
        // A known identical layout is a pure cache hit, including its error state.
        if (pixelUnpackState == state) return;
        var previous = pixelUnpackState;
        // Leave the cache unknown if a driver call throws partway through the update.
        pixelUnpackState = null;
        // Optional diagnostics share the ordinary transition policy; boundary restoration owns its checks.
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (previous?.Alignment != state.Alignment) GL.PixelStore(PixelStoreParameter.UnpackAlignment, state.Alignment);
        if (previous?.RowLength != state.RowLength) GL.PixelStore(PixelStoreParameter.UnpackRowLength, state.RowLength);
        if (previous?.SkipRows != state.SkipRows) GL.PixelStore(PixelStoreParameter.UnpackSkipRows, state.SkipRows);
        if (previous?.SkipPixels != state.SkipPixels) GL.PixelStore(PixelStoreParameter.UnpackSkipPixels, state.SkipPixels);
        if (previous?.SwapBytes != state.SwapBytes) GL.PixelStore(PixelStoreParameter.UnpackSwapBytes, state.SwapBytes ? 1 : 0);
        if (previous?.LsbFirst != state.LsbFirst) GL.PixelStore(PixelStoreParameter.UnpackLsbFirst, state.LsbFirst ? 1 : 0);
        if (previous?.ImageHeight != state.ImageHeight) GL.PixelStore(PixelStoreParameter.UnpackImageHeight, state.ImageHeight);
        if (previous?.SkipImages != state.SkipImages) GL.PixelStore(PixelStoreParameter.UnpackSkipImages, state.SkipImages);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        pixelUnpackState = state;
    }

    /// <summary>Applies a temporary layout and restores the previous layout on disposal.</summary>
    public PixelUnpackScope SetPixelUnpackScope(PixelUnpackState state)
    {
        var previous = GetPixelUnpackState();
        try { SetPixelUnpackState(state); }
        catch (Exception operation)
        {
            try { SetPixelUnpackState(previous); }
            catch (Exception cleanup) { throw new AggregateException(operation, cleanup); }
            throw;
        }
        return new PixelUnpackScope(this, previous);
    }

    /// <summary>Describes row and image layout, source skips and byte ordering for texture and bitmap uploads.</summary>
    public readonly record struct PixelUnpackState(int Alignment, int RowLength = 0, int SkipRows = 0, int SkipPixels = 0, bool SwapBytes = false, bool LsbFirst = false, int ImageHeight = 0, int SkipImages = 0);

    /// <summary>Restores a borrowed unpack layout through the owning cache.</summary>
    public readonly struct PixelUnpackScope : IDisposable
    {
        private readonly StateCache cache;
        private readonly PixelUnpackState previous;

        /// <summary>Records the layout to restore when this scope ends.</summary>
        internal PixelUnpackScope(StateCache cache, PixelUnpackState previous)
        {
            this.cache = cache;
            this.previous = previous;
        }

        /// <summary>Restores the layout, updating the cache along with GL.</summary>
        public void Dispose() => cache.SetPixelUnpackState(previous);
    }
    #endregion
}
