using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes pixel layouts against the existing single selector cache.</summary>
internal static partial class OpenGLPixelLayouts
{
    #region Internal API
    /// <summary>Gets the cached layout, querying GL only when the state is unknown.</summary>
    internal static OpenGlStateCache.PixelPackState GetPack(OpenGlStateCache cache)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        // Acquire only fields absent from this context; observed mutations preserve sibling knowledge.
        return cache.PixelPackKnowledge ??= new OpenGlStateCache.PixelPackState(
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackAlignment, GetPName.PackAlignment),
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackRowLength, GetPName.PackRowLength),
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipRows, GetPName.PackSkipRows),
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipPixels, GetPName.PackSkipPixels),
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSwapBytes, GetPName.PackSwapBytes) != 0,
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackLsbFirst, GetPName.PackLsbFirst) != 0,
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackImageHeight, GetPName.PackImageHeight),
            ReadPackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipImages, GetPName.PackSkipImages));
    }

    /// <summary>Applies a pack layout, omitting driver calls for known unchanged values.</summary>
    internal static void SetPack(OpenGlStateCache cache, OpenGlStateCache.PixelPackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        if (state.Alignment is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(state));
        if (state.RowLength < 0 || state.SkipRows < 0 || state.SkipPixels < 0 || state.ImageHeight < 0 || state.SkipImages < 0) throw new ArgumentOutOfRangeException(nameof(state));
        // A known identical layout is a pure cache hit, including its error state.
        if (cache.PixelPackKnowledge == state) return;
        // Compare independent known fields before withholding only the values this operation changes.
        bool changeAlignment = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackAlignment, out int knownAlignment) || knownAlignment != state.Alignment;
        bool changeRowLength = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackRowLength, out int knownRowLength) || knownRowLength != state.RowLength;
        bool changeSkipRows = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipRows, out int knownSkipRows) || knownSkipRows != state.SkipRows;
        bool changeSkipPixels = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipPixels, out int knownSkipPixels) || knownSkipPixels != state.SkipPixels;
        bool changeSwapBytes = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSwapBytes, out int knownSwapBytes) || knownSwapBytes != (state.SwapBytes ? 1 : 0);
        bool changeLsbFirst = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackLsbFirst, out int knownLsbFirst) || knownLsbFirst != (state.LsbFirst ? 1 : 0);
        bool changeImageHeight = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackImageHeight, out int knownImageHeight) || knownImageHeight != state.ImageHeight;
        bool changeSkipImages = !cache.TryGetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipImages, out int knownSkipImages) || knownSkipImages != state.SkipImages;
        if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
        bool completed = false;
        try
        {
            if (changeAlignment) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackAlignment);
            if (changeRowLength) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackRowLength);
            if (changeSkipRows) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipRows);
            if (changeSkipPixels) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipPixels);
            if (changeSwapBytes) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSwapBytes);
            if (changeLsbFirst) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackLsbFirst);
            if (changeImageHeight) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackImageHeight);
            if (changeSkipImages) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipImages);
            if (changeAlignment) { GL.PixelStore(PixelStoreParameter.PackAlignment, state.Alignment); backend.RequireContext(); }
            if (changeRowLength) { GL.PixelStore(PixelStoreParameter.PackRowLength, state.RowLength); backend.RequireContext(); }
            if (changeSkipRows) { GL.PixelStore(PixelStoreParameter.PackSkipRows, state.SkipRows); backend.RequireContext(); }
            if (changeSkipPixels) { GL.PixelStore(PixelStoreParameter.PackSkipPixels, state.SkipPixels); backend.RequireContext(); }
            if (changeSwapBytes) { GL.PixelStore(PixelStoreParameter.PackSwapBytes, state.SwapBytes ? 1 : 0); backend.RequireContext(); }
            if (changeLsbFirst) { GL.PixelStore(PixelStoreParameter.PackLsbFirst, state.LsbFirst ? 1 : 0); backend.RequireContext(); }
            if (changeImageHeight) { GL.PixelStore(PixelStoreParameter.PackImageHeight, state.ImageHeight); backend.RequireContext(); }
            if (changeSkipImages) { GL.PixelStore(PixelStoreParameter.PackSkipImages, state.SkipImages); backend.RequireContext(); }
            if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
            backend.RequireContext();
            cache.PixelPackKnowledge = state;
            completed = true;
        }
        finally
        {
            // A native callback could reacquire intermediate facts. Failure must withdraw
            // those touched fields again while retaining untouched sibling knowledge.
            if (!completed && backend.IsCurrentContext)
            {
                if (changeAlignment) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackAlignment);
                if (changeRowLength) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackRowLength);
                if (changeSkipRows) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipRows);
                if (changeSkipPixels) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipPixels);
                if (changeSwapBytes) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSwapBytes);
                if (changeLsbFirst) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackLsbFirst);
                if (changeImageHeight) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackImageHeight);
                if (changeSkipImages) cache.ForgetPixelPackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.PackSkipImages);
            }
        }
    }

    /// <summary>Applies a temporary layout and restores the previous layout on disposal.</summary>
    internal static PixelPackScope PackScope(OpenGlStateCache cache, OpenGlStateCache.PixelPackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        var previous = GetPack(cache);
        try { SetPack(cache, state); }
        catch (Exception operation)
        {
            // Foreign context knowledge belongs to that context; leave it untouched.
            try { if (backend.IsCurrentContext) SetPack(cache, previous); }
            catch (Exception cleanup) { throw new AggregateException(operation, cleanup); }
            throw;
        }
        return new PixelPackScope(cache, previous);
    }
    #endregion

    #region Private
    /// <summary>Acquires one genuinely missing field and preserves previously intercepted values.</summary>
    private static int ReadPackField(OpenGlStateCache cache, RHICommandContext backend,
        OpenTK.Graphics.OpenGL.PixelStoreParameter parameter, GetPName query)
    {
        if (cache.TryGetPixelPackField(parameter, out int value)) return value;
        value = ReadLayoutValue(cache, backend, query);
        cache.ObservePixelPackField(parameter, value);
        return value;
    }

    #endregion

    /// <summary>Restores the original context layout without donating state to a replacement context.</summary>
    internal readonly struct PixelPackScope : IDisposable
    {
        private readonly OpenGlStateCache cache;
        private readonly OpenGlStateCache.PixelPackState previous;
        private readonly RHICommandContext backend;
        #region Internal API
        /// <summary>Captures original context and selector facts after the temporary transition succeeds.</summary>
        internal PixelPackScope(OpenGlStateCache cache, OpenGlStateCache.PixelPackState previous)
        {
            this.cache = cache; this.previous = previous; backend = RHICommandContext.Current;
        }
        #endregion
        #region Public API
        /// <summary>Restores only in the original live namespace, withdrawing knowledge otherwise.</summary>
        public void Dispose()
        {
            if (!backend.IsCurrentContext) return;
            SetPack(cache, previous);
        }
        #endregion
    }
}
