using System;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Resolves the admitted graphics and compute footprint through existing selective restoration.</summary>
internal sealed partial class OpenGLBackend
{

    #region Public API
    /// <inheritdoc />
    public bool TryBeginBoundary(RHIBoundaryDescription description, out EngineBoundaryScope? boundary)
    {
        RequireContext();
        cache.RequireOutsideEngineBoundary();
        boundary = null;
        if (!description.ConditionalRenderingInactive || !NativeShaderHandoff.IsInactive) return false;
        bool passes = description.Operations.HasFlag(BoundaryOperations.RenderPasses);
        bool geometry = description.Graphics.Length != 0;
        if (geometry && !cache.TryVerifyInactiveTransformFeedback()) return false;
        var coverage = passes ? OpenGLRenderPassOperations.ClearCoverage.Union(new(dynamic: DynamicDrawStateKnowledge.Viewport))
            : PipelineStateCoverage.Empty;
        var resources = new EngineBoundaryResources(geometry: geometry, framebuffers: passes,
            programSelection: geometry || description.Compute.Length != 0);
        foreach (var reference in description.Graphics)
        {
            var pipeline = reference.RequirePipeline();
            var shader = pipeline.RequireShader();
            coverage = coverage.Union(pipeline.Coverage);
            resources = resources.Union(EngineBoundaryResources.From(shader.Interface.PreparedBindings));
        }
        foreach (var reference in description.Compute)
        {
            var shader = reference.RequirePipeline().RequireShader();
            resources = resources.Union(EngineBoundaryResources.From(shader.Interface.PreparedBindings));
        }
        if (!cache.TryBeginEngineBoundary(new(description.Name, coverage), out boundary, resources)) return false;
        if (passes) renderPassSequence = new(boundary!,this);
        return true;
    }
    #endregion
}
