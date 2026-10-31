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
        var buffers = new int[description.Outputs.Length];
        for (int index = 0; index < buffers.Length; index++)
        {
            var output = description.Outputs[index];
            if ((uint)output.Slot >= (uint)Capabilities.MaxTransformFeedbackBuffers || !slots.Add(output.Slot)
                || output.Range.Length <= 0 || output.Range.Offset % 4 != 0 || output.Range.Length % 4 != 0)
                throw new ArgumentException("Capture slots and ranges must be unique, aligned and within limits.");
            buffers[index] = (int)RequireRange(output.Range.Buffer, output.Range.Offset, output.Range.Length).ResourceId;
        }
        var owner = new GpuTransformFeedback();
        try
        {
            if (!RHIResourceManager.CreateTransformFeedback(new(), owner, out _).HasValue)
            { owner.Dispose(); return new(null, "Transform-feedback initialization failed."); }
            // Configure only the new private candidate; a failed setup never exposes a usable owner.
            // Reuse the resolved selections within this owner-controlled creation interval.
            for (int index = 0; index < buffers.Length; index++)
            {
                var output = description.Outputs[index];
                OpenGLDriverCalls.TransformFeedbackBufferRange(owner.TransformFeedbackId, output.Slot, buffers[index], output.Range.Offset, output.Range.Length);
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
        try { OpenGLDriverCalls.CreateTransformFeedbacks(1, out name); }
        finally { id = name; }
        RequireContext();
        if (name != 0) cache.RecordFeedbackCreated(name);
        return name == 0 ? null : new GpuTransformFeedbackMetadata();
    }
    #endregion
}
