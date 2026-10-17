using System;
using System.Collections.Generic;
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
            coverage = coverage.Union(PipelineStateCoverage.From(pipeline));
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
