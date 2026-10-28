using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes pixel layouts against the existing single selector cache.</summary>
internal static partial class OpenGLPixelLayouts
{
    #region Internal API
    /// <summary>Gets the cached layout, querying GL only when the state is unknown.</summary>
    internal static OpenGlStateCache.PixelUnpackState GetUnpack(OpenGlStateCache cache)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        // Acquire only fields absent from this context; observed mutations preserve sibling knowledge.
        return cache.PixelUnpackKnowledge ??= new OpenGlStateCache.PixelUnpackState(
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackAlignment, GetPName.UnpackAlignment),
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackRowLength, GetPName.UnpackRowLength),
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipRows, GetPName.UnpackSkipRows),
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipPixels, GetPName.UnpackSkipPixels),
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSwapBytes, GetPName.UnpackSwapBytes) != 0,
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackLsbFirst, GetPName.UnpackLsbFirst) != 0,
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackImageHeight, GetPName.UnpackImageHeight),
            ReadUnpackField(cache, backend, OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipImages, GetPName.UnpackSkipImages));
    }

    /// <summary>Applies an unpack layout, omitting driver calls for known unchanged values.</summary>
    internal static void SetUnpack(OpenGlStateCache cache, OpenGlStateCache.PixelUnpackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        if (state.Alignment is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(state));
        if (state.RowLength < 0 || state.SkipRows < 0 || state.SkipPixels < 0 || state.ImageHeight < 0 || state.SkipImages < 0) throw new ArgumentOutOfRangeException(nameof(state));
        // A known identical layout is a pure cache hit, including its error state.
        if (cache.PixelUnpackKnowledge == state) return;
        // Compare independent known fields before withholding only the values this operation changes.
        bool changeAlignment = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackAlignment, out int knownAlignment) || knownAlignment != state.Alignment;
        bool changeRowLength = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackRowLength, out int knownRowLength) || knownRowLength != state.RowLength;
        bool changeSkipRows = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipRows, out int knownSkipRows) || knownSkipRows != state.SkipRows;
        bool changeSkipPixels = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipPixels, out int knownSkipPixels) || knownSkipPixels != state.SkipPixels;
        bool changeSwapBytes = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSwapBytes, out int knownSwapBytes) || knownSwapBytes != (state.SwapBytes ? 1 : 0);
        bool changeLsbFirst = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackLsbFirst, out int knownLsbFirst) || knownLsbFirst != (state.LsbFirst ? 1 : 0);
        bool changeImageHeight = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackImageHeight, out int knownImageHeight) || knownImageHeight != state.ImageHeight;
        bool changeSkipImages = !cache.TryGetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipImages, out int knownSkipImages) || knownSkipImages != state.SkipImages;
        if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
        bool completed = false;
        try
        {
            if (changeAlignment) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackAlignment);
            if (changeRowLength) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackRowLength);
            if (changeSkipRows) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipRows);
            if (changeSkipPixels) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipPixels);
            if (changeSwapBytes) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSwapBytes);
            if (changeLsbFirst) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackLsbFirst);
            if (changeImageHeight) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackImageHeight);
            if (changeSkipImages) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipImages);
            if (changeAlignment) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackAlignment, state.Alignment); backend.RequireContext(); }
            if (changeRowLength) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackRowLength, state.RowLength); backend.RequireContext(); }
            if (changeSkipRows) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackSkipRows, state.SkipRows); backend.RequireContext(); }
            if (changeSkipPixels) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackSkipPixels, state.SkipPixels); backend.RequireContext(); }
            if (changeSwapBytes) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackSwapBytes, state.SwapBytes ? 1 : 0); backend.RequireContext(); }
            if (changeLsbFirst) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackLsbFirst, state.LsbFirst ? 1 : 0); backend.RequireContext(); }
            if (changeImageHeight) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackImageHeight, state.ImageHeight); backend.RequireContext(); }
            if (changeSkipImages) { OpenGLDriverCalls.PixelStore(PixelStoreParameter.UnpackSkipImages, state.SkipImages); backend.RequireContext(); }
            if (GlDebug.CheckStateTransitions && !cache.RestoringBoundary) cache.CheckBoundaryNativeError();
            backend.RequireContext();
            cache.PixelUnpackKnowledge = state;
            completed = true;
        }
        finally
        {
            // A native callback could reacquire intermediate facts. Failure must withdraw
            // those touched fields again while retaining untouched sibling knowledge.
            if (!completed && backend.IsCurrentContext)
            {
                if (changeAlignment) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackAlignment);
                if (changeRowLength) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackRowLength);
                if (changeSkipRows) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipRows);
                if (changeSkipPixels) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipPixels);
                if (changeSwapBytes) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSwapBytes);
                if (changeLsbFirst) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackLsbFirst);
                if (changeImageHeight) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackImageHeight);
                if (changeSkipImages) cache.ForgetPixelUnpackField(OpenTK.Graphics.OpenGL.PixelStoreParameter.UnpackSkipImages);
            }
        }
    }

    /// <summary>Applies a temporary layout and restores the previous layout on disposal.</summary>
    internal static PixelUnpackScope UnpackScope(OpenGlStateCache cache, OpenGlStateCache.PixelUnpackState state)
    {
        var backend = RHICommandContext.Current;
        backend.RequireContext();
        var previous = GetUnpack(cache);
        try { SetUnpack(cache, state); }
        catch (Exception operation)
        {
            // Foreign context knowledge belongs to that context; leave it untouched.
            try { if (backend.IsCurrentContext) SetUnpack(cache, previous); }
            catch (Exception cleanup) { throw new AggregateException(operation, cleanup); }
            throw;
        }
        return new PixelUnpackScope(cache, previous);
    }
    #endregion

    #region Private
    /// <summary>Acquires one genuinely missing field and preserves previously intercepted values.</summary>
    private static int ReadUnpackField(OpenGlStateCache cache, RHICommandContext backend,
        OpenTK.Graphics.OpenGL.PixelStoreParameter parameter, GetPName query)
    {
        if (cache.TryGetPixelUnpackField(parameter, out int value)) return value;
        value = ReadLayoutValue(cache, backend, query);
        cache.ObservePixelUnpackField(parameter, value);
        return value;
    }

    #endregion

    /// <summary>Restores the original context layout without donating state to a replacement context.</summary>
    internal readonly struct PixelUnpackScope : IDisposable
    {
        private readonly OpenGlStateCache cache;
        private readonly OpenGlStateCache.PixelUnpackState previous;
        private readonly RHICommandContext backend;
        #region Internal API
        /// <summary>Captures original context and selector facts after the temporary transition succeeds.</summary>
        internal PixelUnpackScope(OpenGlStateCache cache, OpenGlStateCache.PixelUnpackState previous)
        {
            this.cache = cache; this.previous = previous; backend = RHICommandContext.Current;
        }
        #endregion
        #region Public API
        /// <summary>Restores only in the original live namespace, withdrawing knowledge otherwise.</summary>
        public void Dispose()
        {
            if (!backend.IsCurrentContext) return;
            SetUnpack(cache, previous);
        }
        #endregion
    }
}
