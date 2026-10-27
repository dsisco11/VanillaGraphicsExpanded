using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes pixel layouts against the existing single selector cache.</summary>
internal static partial class OpenGLPixelLayouts
{
    #region Internal API
    /// <summary>Gets the cached layout, querying GL only when the state is unknown.</summary>
    internal static StateCache.PixelPackState GetPack(StateCache cache)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        // External rendering boundaries must invalidate the cache before borrowing state.
        return cache.PixelPackKnowledge ??= new StateCache.PixelPackState(
            ReadLayoutValue(cache, backend, GetPName.PackAlignment),
            ReadLayoutValue(cache, backend, GetPName.PackRowLength),
            ReadLayoutValue(cache, backend, GetPName.PackSkipRows),
            ReadLayoutValue(cache, backend, GetPName.PackSkipPixels),
            ReadLayoutValue(cache, backend, GetPName.PackSwapBytes) != 0,
            ReadLayoutValue(cache, backend, GetPName.PackLsbFirst) != 0,
            ReadLayoutValue(cache, backend, GetPName.PackImageHeight),
            ReadLayoutValue(cache, backend, GetPName.PackSkipImages));
    }

    /// <summary>Applies a pack layout, omitting driver calls for known unchanged values.</summary>
    internal static void SetPack(StateCache cache, StateCache.PixelPackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        if (state.Alignment is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(state));
        if (state.RowLength < 0 || state.SkipRows < 0 || state.SkipPixels < 0 || state.ImageHeight < 0 || state.SkipImages < 0) throw new ArgumentOutOfRangeException(nameof(state));
        // A known identical layout is a pure cache hit, including its error state.
        if (cache.PixelPackKnowledge == state) return;
        var previous = cache.PixelPackKnowledge;
        // Leave the cache unknown if a driver call throws partway through the update.
        cache.PixelPackKnowledge = null;
        // Optional diagnostics share the ordinary transition policy; boundary restoration owns its checks.
        if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
        if (previous?.Alignment != state.Alignment) { GL.PixelStore(PixelStoreParameter.PackAlignment, state.Alignment); backend.RequireContext(); }
        if (previous?.RowLength != state.RowLength) { GL.PixelStore(PixelStoreParameter.PackRowLength, state.RowLength); backend.RequireContext(); }
        if (previous?.SkipRows != state.SkipRows) { GL.PixelStore(PixelStoreParameter.PackSkipRows, state.SkipRows); backend.RequireContext(); }
        if (previous?.SkipPixels != state.SkipPixels) { GL.PixelStore(PixelStoreParameter.PackSkipPixels, state.SkipPixels); backend.RequireContext(); }
        if (previous?.SwapBytes != state.SwapBytes) { GL.PixelStore(PixelStoreParameter.PackSwapBytes, state.SwapBytes ? 1 : 0); backend.RequireContext(); }
        if (previous?.LsbFirst != state.LsbFirst) { GL.PixelStore(PixelStoreParameter.PackLsbFirst, state.LsbFirst ? 1 : 0); backend.RequireContext(); }
        if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
        if (previous?.ImageHeight != state.ImageHeight) { GL.PixelStore(PixelStoreParameter.PackImageHeight, state.ImageHeight); backend.RequireContext(); }
        if (previous?.SkipImages != state.SkipImages) { GL.PixelStore(PixelStoreParameter.PackSkipImages, state.SkipImages); backend.RequireContext(); }
        backend.RequireContext();
        cache.PixelPackKnowledge = state;
    }

    /// <summary>Applies a temporary layout and restores the previous layout on disposal.</summary>
    internal static PixelPackScope PackScope(StateCache cache, StateCache.PixelPackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        var previous = GetPack(cache);
        try { SetPack(cache, state); }
        catch (Exception operation)
        {
            try { if (backend.IsCurrentContext) SetPack(cache, previous); else cache.DirtyPixelPackState(); }
            catch (Exception cleanup) { throw new AggregateException(operation, cleanup); }
            throw;
        }
        return new PixelPackScope(cache, previous);
    }
    #endregion

    /// <summary>Restores the original context layout without donating state to a replacement context.</summary>
    internal readonly struct PixelPackScope : IDisposable
    {
        private readonly StateCache cache;
        private readonly StateCache.PixelPackState previous;
        private readonly RHICommandContext backend;
        #region Internal API
        /// <summary>Captures original context and selector facts after the temporary transition succeeds.</summary>
        internal PixelPackScope(StateCache cache, StateCache.PixelPackState previous)
        {
            this.cache = cache; this.previous = previous; backend = RHICommandContext.Current;
        }
        #endregion
        #region Public API
        /// <summary>Restores only in the original live namespace, withdrawing knowledge otherwise.</summary>
        public void Dispose()
        {
            if (!backend.IsCurrentContext) { cache.DirtyPixelPackState(); return; }
            SetPack(cache, previous);
        }
        #endregion
    }
}
