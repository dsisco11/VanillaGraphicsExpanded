using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
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
        var coverage = new PipelineStateCoverage(clearColor: true);
        bool hasPipeline = false;
        foreach (var pipeline in pipelines)
        {
            coverage = coverage.Union(PipelineStateCoverage.From(pipeline));
            hasPipeline = true;
        }
        // A clear-only pass has no shader executable, but its load operations still need
        // complete drawing-state restoration. Draw remains impossible without a declared pipeline.
        if (!hasPipeline) coverage = new(DepthStateKnowledge.All, RasterizerStateKnowledge.All,
            PrimitiveAssemblyStateKnowledge.All, DynamicDrawStateKnowledge.All, BlendStateKnowledge.All,
            clearColor: true, completeGraphics: true);
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
