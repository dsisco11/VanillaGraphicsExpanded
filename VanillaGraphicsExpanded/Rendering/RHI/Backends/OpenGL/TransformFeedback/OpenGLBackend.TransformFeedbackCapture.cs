using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Keeps capture selection and output guards across the complete begin/end interval.</summary>
internal sealed partial class OpenGLBackend
{
    private FeedbackCapture? feedbackCapture;
    private bool feedbackTransition;
    #region Public API
    /// <inheritdoc />
    public IDisposable CaptureTransformFeedback(GpuTransformFeedbackRef feedback, CapturePrimitive primitive)
    {
        BeginTransformFeedback(feedback, primitive);
        return new FeedbackScope(this, feedbackCapture!);
    }
    /// <inheritdoc />
    public void BeginTransformFeedback(GpuTransformFeedbackRef feedback, CapturePrimitive primitive)
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (feedbackTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        feedbackTransition = true;
        try
        {
            RequireContext();
            if (feedbackCapture is not null) throw new InvalidOperationException("Capture is already active.");
            var nativePrimitive = primitive switch
            {
                CapturePrimitive.Points => TransformFeedbackPrimitiveType.Points,
                CapturePrimitive.Lines => TransformFeedbackPrimitiveType.Lines,
                CapturePrimitive.Triangles => TransformFeedbackPrimitiveType.Triangles,
                _ => throw new ArgumentOutOfRangeException(nameof(primitive))
            };
            var owner = feedback.RequireResource();
            owner.RequireOriginalContext();
            if (!owner.IsValid || !feedbackOutputs.TryGetValue(owner, out var description))
                throw new InvalidOperationException("The configured capture resource is unavailable.");
            var retained = new List<GpuResource>();
            int previous = OpenGLFeedbackBindings.Get(cache);
            try
            {
                owner.RetainPassReference(); retained.Add(owner);
                foreach (var output in description.Outputs)
                {
                    var buffer = RequireRange(output.Range.Buffer, output.Range.Offset, output.Range.Length);
                    buffer.RetainPassReference(); retained.Add(buffer);
                    // Resolve owner-following inputs again at execution; never capture into a former allocation.
                    GL.TransformFeedbackBufferRange(owner.TransformFeedbackId, output.Slot, (int)buffer.ResourceId,
                        output.Range.Offset, output.Range.Length);
                    RequireContext();
                }
                OpenGLFeedbackBindings.Bind(cache, owner.TransformFeedbackId);
                OpenGLDriverCalls.BeginTransformFeedback(nativePrimitive);
                RequireContext();
                cache.RecordFeedbackActivity(true, false);
                feedbackCapture = new(previous, retained);
            }
            catch
            {
                // Native failure is never retried; restore only while the original namespace is current.
                try
                {
                    if (IsCurrentContext) OpenGLFeedbackBindings.Bind(cache, previous);
                    else cache.TransformFeedbackKnowledge = null;
                }
                catch { cache.TransformFeedbackKnowledge = null; }
                finally { foreach (var item in retained) item.ReleasePassReference(); }
                throw;
            }
        }
        finally { feedbackTransition = false; }
    }
    /// <inheritdoc />
    public void EndTransformFeedback()
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (feedbackTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        feedbackTransition = true;
        try
        {
            if (HasRetiredOrigin)
            {
                var retired = feedbackCapture ?? throw new InvalidOperationException("Capture is not active.");
                feedbackCapture = null;
                foreach (var item in retired.Retained) item.ReleasePassReference();
                return;
            }
            RequireContext();
            var capture = feedbackCapture ?? throw new InvalidOperationException("Capture is not active.");
            Exception? failure = null;
            try { OpenGLDriverCalls.EndTransformFeedback(); RequireContext(); cache.RecordFeedbackActivity(false, false); }
            catch (Exception error) { failure = error; throw; }
            finally
            {
                feedbackCapture = null;
                try
                {
                    if (IsCurrentContext) OpenGLFeedbackBindings.Bind(cache, capture.Previous);
                    else cache.TransformFeedbackKnowledge = null;
                }
                catch { cache.TransformFeedbackKnowledge = null; if (failure is null) throw; }
                finally { foreach (var item in capture.Retained) item.ReleasePassReference(); }
            }
        }
        finally { feedbackTransition = false; }
    }
    /// <inheritdoc />
    public void PauseTransformFeedback()
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (feedbackTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        feedbackTransition = true;
        try
        {
            RequireContext();
            var capture = feedbackCapture ?? throw new InvalidOperationException("Capture is not active.");
            if (capture.Paused) throw new InvalidOperationException("Capture is already paused.");
            OpenGLDriverCalls.PauseTransformFeedback(); RequireContext(); cache.RecordFeedbackActivity(true, true); capture.Paused = true;
        }
        finally { feedbackTransition = false; }
    }
    /// <inheritdoc />
    public void ResumeTransformFeedback()
    {
        // Native callbacks must not reenter an unfinished execution-state transition.
        if (feedbackTransition) throw new InvalidOperationException("An auxiliary operation is already in progress.");
        feedbackTransition = true;
        try
        {
            RequireContext();
            var capture = feedbackCapture ?? throw new InvalidOperationException("Capture is not active.");
            if (!capture.Paused) throw new InvalidOperationException("Capture is not paused.");
            OpenGLDriverCalls.ResumeTransformFeedback(); RequireContext(); cache.RecordFeedbackActivity(true, false); capture.Paused = false;
        }
        finally { feedbackTransition = false; }
    }
    #endregion
    #region Private
    /// <summary>Ends only its own capture, never a subsequent interval selected by another caller.</summary>
    private sealed class FeedbackScope(OpenGLBackend backend, FeedbackCapture capture) : IDisposable
    {
        private bool disposed;
        #region Public API
        /// <summary>Unwinds the matching capture once; foreign contexts never receive native cleanup.</summary>
        public void Dispose()
        {
            if (disposed) return;
            if (!backend.HasRetiredOrigin) backend.RequireContext();
            if (!ReferenceEquals(backend.feedbackCapture, capture))
                throw new InvalidOperationException("The capture scope no longer owns the active interval.");
            backend.EndTransformFeedback();
            disposed = true;
        }
        #endregion
    }
    /// <summary>Retains only execution-lifetime state, not a second metadata or binding cache.</summary>
    private sealed class FeedbackCapture(int previous, List<GpuResource> retained)
    {
        internal readonly int Previous = previous;
        internal readonly List<GpuResource> Retained = retained;
        internal bool Paused;
    }
    #endregion
}
