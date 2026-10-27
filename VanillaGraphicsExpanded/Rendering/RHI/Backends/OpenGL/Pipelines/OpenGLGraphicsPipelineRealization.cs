using VanillaGraphicsExpanded.Rendering.Pipeline;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Contains OpenGL relevance and restoration data for one immutable graphics description.</summary>
internal sealed class OpenGLGraphicsPipelineRealization : IRHIGraphicsPipelineRealization
{
    #region Internal API
    /// <summary>Gets the groups consumed by this executable and description.</summary>
    internal GraphicsStateRelevance Relevance { get; }
    /// <summary>Gets the complete native write footprint admitted at boundary entry.</summary>
    internal PipelineStateCoverage Coverage { get; }
    /// <summary>Derives native coverage once without allocating an independent native resource.</summary>
    internal OpenGLGraphicsPipelineRealization(GraphicsStateRelevance relevance)
    { Relevance = relevance; Coverage = PipelineStateCoverage.From(relevance); }
    #endregion
}
