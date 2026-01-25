using System;
using System.Diagnostics;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// RAII wrapper around an OpenGL transform feedback object (TFO).
/// Deletion is deferred to <see cref="GpuResourceManager"/> when available.
/// All methods require a current GL context on the calling thread.
/// </summary>
internal sealed class GpuTransformFeedback : GpuResource, IDisposable
{
    private int transformFeedbackId;

    protected override nint ResourceId
    {
        get => transformFeedbackId;
        set => transformFeedbackId = (int)value;
    }

    protected override GpuResourceKind ResourceKind => GpuResourceKind.TransformFeedback;

    /// <summary>
    /// Gets the underlying OpenGL transform feedback id.
    /// </summary>
    public int TransformFeedbackId => transformFeedbackId;

    /// <summary>
    /// Returns <c>true</c> when the transform feedback has a non-zero id and has not been disposed.
    /// </summary>
    public new bool IsValid => transformFeedbackId != 0 && !IsDisposed;

    private GpuTransformFeedback(int transformFeedbackId)
    {
        this.transformFeedbackId = transformFeedbackId;
    }

    /// <summary>
    /// Creates a new transform feedback object.
    /// </summary>
    public static GpuTransformFeedback Create(string? debugName = null)
    {
        int id = GL.GenTransformFeedback();
        if (id == 0)
        {
            throw new InvalidOperationException("glGenTransformFeedbacks failed.");
        }

        var tf = new GpuTransformFeedback(id);
        tf.SetDebugName(debugName);
        return tf;
    }

    /// <summary>
    /// Sets the debug label for this transform feedback object (debug builds only).
    /// </summary>
    public override void SetDebugName(string? debugName)
    {
#if DEBUG
        if (transformFeedbackId != 0)
        {
            GlDebug.TryLabel(ObjectLabelIdentifier.TransformFeedback, transformFeedbackId, debugName);
        }
#endif
    }

    /// <summary>
    /// Binds this transform feedback object to <see cref="TransformFeedbackTarget.TransformFeedback"/>.
    /// </summary>
    public void Bind()
    {
        if (!IsValid)
        {
            Debug.WriteLine("[GpuTransformFeedback] Attempted to bind disposed or invalid transform feedback");
            return;
        }

        GlStateCache.Current.BindTransformFeedback(transformFeedbackId);
    }

    /// <summary>
    /// Attempts to bind this transform feedback object. Returns <c>false</c> if invalid.
    /// </summary>
    public bool TryBind()
    {
        if (!IsValid)
        {
            return false;
        }

        GlStateCache.Current.BindTransformFeedback(transformFeedbackId);
        return true;
    }

    /// <summary>
    /// Unbinds any transform feedback object from <see cref="TransformFeedbackTarget.TransformFeedback"/>.
    /// </summary>
    public void Unbind()
    {
        GlStateCache.Current.BindTransformFeedback(0);
    }

    /// <summary>
    /// Binds this transform feedback object and returns a scope that restores the previous binding when disposed.
    /// </summary>
    public BindingScope BindScope()
    {
        var gl = GlStateCache.Current;
        var scope = gl.BindTransformFeedbackScope(transformFeedbackId);
        return new BindingScope(scope);
    }

    /// <summary>
    /// Binds a buffer to a transform feedback binding point (indexed) via <c>glBindBufferBase</c>.
    /// </summary>
    public void BindBufferBase(int index, int bufferId)
    {
        if (!IsValid)
        {
            return;
        }

        using var _ = BindScope();
        GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, index, bufferId);
    }

    /// <summary>
    /// Binds a buffer to a transform feedback binding point (indexed) via <c>glBindBufferBase</c>.
    /// </summary>
    public void BindBufferBase(int index, GpuBufferObject buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        BindBufferBase(index, buffer.BufferId);
    }

    /// <summary>
    /// Binds a buffer range to a transform feedback binding point (indexed) via <c>glBindBufferRange</c>.
    /// </summary>
    public void BindBufferRange(int index, int bufferId, nint offsetBytes, nint sizeBytes)
    {
        if (!IsValid)
        {
            return;
        }

        using var _ = BindScope();
        GL.BindBufferRange(BufferRangeTarget.TransformFeedbackBuffer, index, bufferId, (IntPtr)offsetBytes, (IntPtr)sizeBytes);
    }

    /// <summary>
    /// Begins transform feedback capture.
    /// </summary>
    /// <remarks>
    /// The caller must ensure rasterizer discard state is configured as intended.
    /// </remarks>
    public void Begin(TransformFeedbackPrimitiveType primitiveMode)
    {
        if (!IsValid)
        {
            return;
        }

        using var _ = BindScope();
        GL.BeginTransformFeedback(primitiveMode);
    }

    /// <summary>
    /// Ends transform feedback capture.
    /// </summary>
    public void End()
    {
        if (!IsValid)
        {
            return;
        }

        using var _ = BindScope();
        GL.EndTransformFeedback();
    }

    /// <summary>
    /// Pauses transform feedback capture.
    /// </summary>
    public void Pause()
    {
        if (!IsValid)
        {
            return;
        }

        using var _ = BindScope();
        GL.PauseTransformFeedback();
    }

    /// <summary>
    /// Resumes transform feedback capture.
    /// </summary>
    public void Resume()
    {
        if (!IsValid)
        {
            return;
        }

        using var _ = BindScope();
        GL.ResumeTransformFeedback();
    }

    /// <summary>
    /// Scope that restores the previous transform feedback binding when disposed.
    /// </summary>
    public readonly struct BindingScope : IDisposable
    {
        private readonly GlStateCache.TransformFeedbackScope scope;

        public BindingScope(GlStateCache.TransformFeedbackScope scope)
        {
            this.scope = scope;
        }

        public void Dispose()
        {
            scope.Dispose();
        }
    }
}
