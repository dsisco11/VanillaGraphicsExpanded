using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Dispatches fixed compute work under owner lifetimes, retaining dynamic argument and context handling.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void Dispatch(RHIComputePipelineRef pipeline, in RHIDispatch dispatch, RHIResourceBindings? resources)
    {
        RequireContext();
        var shader = pipeline.RequirePipeline().RequireShader();
        var maximum = NativeCapabilities.MaxComputeWorkGroupCount;
        if (maximum.Length != 3 || dispatch.X < 0 || dispatch.Y < 0 || dispatch.Z < 0
            || dispatch.X > maximum[0] || dispatch.Y > maximum[1] || dispatch.Z > maximum[2])
            throw new ArgumentOutOfRangeException(nameof(dispatch), "Compute workgroup counts exceed cached device limits.");
        SelectResourceBindings(shader, resources);
        try
        {
            if (resources is null) ApplyExecutable(shader);
            else PublishResourceBindings(shader, reuseCommittedUniforms: true);
            MarkBindingExecution();
            GL.DispatchCompute(dispatch.X, dispatch.Y, dispatch.Z);
            RequireContext();
        }
        finally { ClearResourceBindings(); }
    }

    /// <inheritdoc />
    public void DispatchIndirect(RHIComputePipelineRef pipeline, in RHIBufferRange arguments, RHIResourceBindings? resources)
    {
        RequireContext();
        var shader = pipeline.RequirePipeline().RequireShader();
        if (arguments.Offset % 4 != 0 || arguments.Length < 12)
            throw new ArgumentException("Compute arguments require an aligned twelve-byte interval.", nameof(arguments));
        var buffer = RequireRange(arguments.Buffer, arguments.Offset, arguments.Length);
        SelectResourceBindings(shader, resources);
        try
        {
            if (resources is null) ApplyExecutable(shader);
            else PublishResourceBindings(shader, reuseCommittedUniforms: true);
            // The command owns this selector scope. Resource owners keep argument storage valid through dispatch.
            using var binding = cache.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.DispatchIndirectBuffer, (int)buffer.ResourceId);
            RequireContext();
            MarkBindingExecution();
            GL.DispatchComputeIndirect(arguments.Offset);
            RequireContext();
        }
        finally { ClearResourceBindings(); }
    }
    #endregion
}
