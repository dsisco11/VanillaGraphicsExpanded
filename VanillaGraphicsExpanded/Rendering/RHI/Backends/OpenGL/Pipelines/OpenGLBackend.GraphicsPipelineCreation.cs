using System;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Realizes fixed graphics application data from already published executable facts.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public RHICreationResult<RHIGraphicsPipeline> CreateGraphicsPipeline(RHIGraphicsPipelineDescription description)
    {
        RequireContext();
        ArgumentNullException.ThrowIfNull(description);
        var shader = description.Shader.RequireShader();
        if (shader.Description.Kind != ShaderKind.Graphics)
            throw new ArgumentException("A graphics pipeline requires a graphics executable.", nameof(description));
        var graphics = shader.Interface.Graphics
            ?? throw new InvalidOperationException("The executable has no published graphics interface.");
        PipelineDescriptionValidation.Validate(description, NativeCapabilities);
        GraphicsInterfaceValidation.Validate(description, graphics);
        var relevance = new GraphicsStateRelevance(description, NativeCapabilities, graphics);
        return new(new RHIGraphicsPipeline(this, description, shader.Interface.PreparedBindings, relevance), null);
    }
    #endregion
}
