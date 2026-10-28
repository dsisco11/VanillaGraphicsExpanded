using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes named capture objects and retains their authored buffer selections.</summary>
internal sealed partial class OpenGLBackend
{
    private readonly ConditionalWeakTable<GpuTransformFeedback, RHITransformFeedbackDescription> feedbackOutputs = new();
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<GpuTransformFeedback> CreateTransformFeedback(RHITransformFeedbackDescription description)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(description);
        var slots = new HashSet<int>();
        foreach (var output in description.Outputs)
        {
            if ((uint)output.Slot >= (uint)Capabilities.MaxTransformFeedbackBuffers || !slots.Add(output.Slot)
                || output.Range.Length <= 0 || output.Range.Offset % 4 != 0 || output.Range.Length % 4 != 0)
                throw new ArgumentException("Capture slots and ranges must be unique, aligned and within limits.");
            RequireRange(output.Range.Buffer, output.Range.Offset, output.Range.Length);
        }
        var owner = new GpuTransformFeedback();
        try
        {
            if (!GpuResourceManager.CreateTransformFeedback(new(), owner, out _).HasValue)
            { owner.Dispose(); return new(null, "Transform-feedback initialization failed."); }
            // Configure only the new private candidate; a failed setup never exposes a usable owner.
            foreach (var output in description.Outputs)
            {
                var buffer = RequireRange(output.Range.Buffer, output.Range.Offset, output.Range.Length);
                GL.TransformFeedbackBufferRange(owner.TransformFeedbackId, output.Slot, (int)buffer.ResourceId, output.Range.Offset, output.Range.Length);
                RequireContext();
            }
            feedbackOutputs.Add(owner, description);
            return new(owner, null);
        }
        catch { try { owner.Dispose(); } catch { } throw; }
    }
    #endregion
    #region Internal API
    /// <summary>Creates a realized candidate without binding, descriptor scans or status queries.</summary>
    public GpuTransformFeedbackMetadata? InitializeTransformFeedback(out nint id)
    {
        RequireContext();
        int name = 0;
        try { GL.CreateTransformFeedbacks(1, out name); }
        finally { id = name; }
        RequireContext();
        if (name != 0) cache.RecordFeedbackCreated(name);
        return name == 0 ? null : new GpuTransformFeedbackMetadata();
    }
    #endregion
}
