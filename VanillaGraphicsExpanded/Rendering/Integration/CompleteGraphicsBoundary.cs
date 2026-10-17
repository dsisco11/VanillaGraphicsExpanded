using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Preserves complete graphics and prepared-resource state at an owned graphics interruption.</summary>
internal static class CompleteGraphicsBoundary
{
    #region Public API
    /// <summary>Checks excluded operations and resolves the full footprint before any rendering mutation.</summary>
    /// <param name="conditionalRenderingInactive">An explicit caller invariant; native conditional-render activity has no query.</param>
    internal static bool TryRun(string name, IEnumerable<GraphicsPipelineDesc> pipelines,
        IEnumerable<GpuProgram> programs, bool conditionalRenderingInactive, Action<EngineBoundaryScope> operation)
    {
        ArgumentNullException.ThrowIfNull(pipelines);
        // Descriptor-only callers have no verified emitted primitive, so use conservative stage fallback.
        return TryRunCore(name, pipelines.Select(p => PipelineStateCoverage.From(
            GraphicsStateRelevance.Derive(p, GpuSupport.Graphics))), programs, conditionalRenderingInactive, operation);
    }

    /// <summary>Unions exact prepared coverage and executable resource footprints before managed submission.</summary>
    internal static bool TryRunPrepared(string name, IEnumerable<GraphicsPipeline> pipelines,
        bool conditionalRenderingInactive, Action<EngineBoundaryScope> operation)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        ArgumentNullException.ThrowIfNull(pipelines);
        var prepared = pipelines.ToArray();
        foreach (var pipeline in prepared) { ArgumentNullException.ThrowIfNull(pipeline); pipeline.Validate(); }
        return TryRunCore(name, prepared.Select(p => p.Coverage), prepared.Select(p => p.Shader).Distinct(),
            conditionalRenderingInactive, operation);
    }
    #endregion

    #region Private
    /// <summary>Captures the union of possible writes plus independent clear helpers, retaining existing admission and cleanup.</summary>
    private static bool TryRunCore(string name, IEnumerable<PipelineStateCoverage> pipelines,
        IEnumerable<GpuProgram> programs, bool conditionalRenderingInactive, Action<EngineBoundaryScope> operation)
    {
        var cache = StateCache.Current;
        cache.RequireOutsideEngineBoundary();
        ArgumentNullException.ThrowIfNull(pipelines);
        ArgumentNullException.ThrowIfNull(programs);
        ArgumentNullException.ThrowIfNull(operation);
        if (!conditionalRenderingInactive || !cache.TryVerifyInactiveTransformFeedback()) return false;
        // Pass descriptions arrive later. Retain the bounded clear envelope even for preserving passes.
        var coverage = RenderPassOperations.ClearCoverage.Union(new(dynamic: DynamicDrawStateKnowledge.Viewport,
            clearColor: true));
        foreach (var pipeline in pipelines)
        {
            coverage = coverage.Union(pipeline);
        }
        var resources = new EngineBoundaryResources(textures: [(0, OpenTK.Graphics.OpenGL.TextureTarget.Texture2D)]);
        foreach (var program in programs)
        {
            // Existing preparation owns reflection; entry only borrows the resulting resource footprint.
            if (program.IsRetired || program.RequiresPreparation) return false;
            var prepared = program.ProgramLayout.BinaryInterface?.PreparedBindings;
            if (prepared is null) return false;
            resources = resources.Union(EngineBoundaryResources.From(prepared));
        }
        return EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration(name, coverage), resources, operation);
    }
    #endregion
}
