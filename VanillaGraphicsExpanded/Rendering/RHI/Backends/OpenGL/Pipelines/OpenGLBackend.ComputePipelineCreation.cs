using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Validates immutable compute application data without linking or reflecting another executable.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<RHIComputePipeline> CreateComputePipeline(RHIComputePipelineDescription description)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(description);
        var shader = description.Shader.RequireShader();
        if (shader.Description.Kind != ShaderKind.Compute)
            throw new ArgumentException("A compute pipeline requires a compute executable.", nameof(description));
        if (GpuResourceManager.RequireProgramSnapshot(shader.Program).ComputeLocalSize is null)
            throw new InvalidOperationException("The compute executable has no published workgroup size.");
        return new(new RHIComputePipeline(this, description), null);
    }
    #endregion
}
