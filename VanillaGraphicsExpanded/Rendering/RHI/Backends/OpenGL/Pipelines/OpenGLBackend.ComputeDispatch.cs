using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Owns compute executable selection and native dispatch using existing cache knowledge and resource guards.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void Dispatch(RHIComputePipelineRef pipeline, in RHIDispatch dispatch, RHIResourceBindings? resources)
    {
        RequireContext();
        var realization = pipeline.RequirePipeline();
        var shader = realization.RequireShader();
        var maximum = NativeCapabilities.MaxComputeWorkGroupCount;
        if (maximum.Length != 3 || dispatch.X < 0 || dispatch.Y < 0 || dispatch.Z < 0
            || dispatch.X > maximum[0] || dispatch.Y > maximum[1] || dispatch.Z > maximum[2])
            throw new ArgumentOutOfRangeException(nameof(dispatch), "Compute workgroup counts exceed cached device limits.");
        realization.BeginSubmission();
        try
        {
            BeginBindingExecution(shader, resources);
            try
            {
                if (resources is null) ApplyExecutable(shader);
                else PublishResourceBindings(shader, resources, reuseCommittedUniforms: true);
                shader.RequireUsable();
                MarkBindingExecution(shader, resources);
                GL.DispatchCompute(dispatch.X, dispatch.Y, dispatch.Z);
                RequireContext();
            }
            finally { ReleaseExecutionResources(); }
        }
        finally { realization.EndSubmission(); }
    }
    /// <inheritdoc />
    public void DispatchIndirect(RHIComputePipelineRef pipeline, in RHIBufferRange arguments, RHIResourceBindings? resources)
    {
        RequireContext();
        var realization = pipeline.RequirePipeline();
        var shader = realization.RequireShader();
        if (arguments.Offset % 4 != 0 || arguments.Length < 12)
            throw new ArgumentException("Compute arguments require an aligned twelve-byte interval.", nameof(arguments));
        var buffer = RequireRange(arguments.Buffer, arguments.Offset, arguments.Length);
        realization.BeginSubmission();
        try
        {
            BeginBindingExecution(shader, resources);
            try
            {
                buffer.RetainPassReference();
                try
                {
                    if (resources is null) ApplyExecutable(shader);
                    else PublishResourceBindings(shader, resources, reuseCommittedUniforms: true);
                    // Indirect dispatch is a bound-only native operation. The backend owns selector restoration.
                    using var binding = cache.BindBufferScope(OpenTK.Graphics.OpenGL.BufferTarget.DispatchIndirectBuffer, (int)buffer.ResourceId);
                    RequireContext();
                    shader.RequireUsable();
                    RequireRange(arguments.Buffer, arguments.Offset, arguments.Length);
                    MarkBindingExecution(shader, resources);
                    GL.DispatchComputeIndirect(arguments.Offset);
                    RequireContext();
                }
                finally { buffer.ReleasePassReference(); }
            }
            finally { ReleaseExecutionResources(); }
        }
        finally { realization.EndSubmission(); }
    }
    #endregion

}
