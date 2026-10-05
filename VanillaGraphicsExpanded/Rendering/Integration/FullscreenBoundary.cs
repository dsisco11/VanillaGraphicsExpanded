using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Composes declared fullscreen passes and prepared resource footprints at an engine entry.</summary>
internal static class FullscreenBoundary
{
    #region Public API
    /// <summary>Resolves the complete interruption before allocation or drawing, preserving each participating shader's inputs.</summary>
    internal static bool TryRun(string name, IEnumerable<GlPipelineDesc> pipelines,
        IEnumerable<GpuProgram> programs, Action<EngineBoundaryScope> operation)
    {
        StateCache.Current.RequireOutsideEngineBoundary();
        var coverage = new PipelineStateCoverage(dynamic: DynamicDrawStateKnowledge.Viewport, clearColor: true);
        foreach (var pipeline in pipelines) coverage = coverage.Union(PipelineStateCoverage.From(pipeline));
        // Texture allocation helpers temporarily use unit zero even when a sampler is optimized out.
        var resources = new EngineBoundaryResources(textures: [(0, OpenTK.Graphics.OpenGL.TextureTarget.Texture2D)]);
        foreach (var program in programs)
        {
            // Preparation belongs before entry; no fallback may silently expand the saved footprint.
            if (program.IsRetired || program.RequiresPreparation) return false;
            var prepared = program.ProgramLayout.BinaryInterface?.PreparedBindings;
            if (prepared is null) return false;
            resources = resources.Union(EngineBoundaryResources.From(prepared));
        }
        return EngineBoundaryExecution.TryRun(new EngineBoundaryDeclaration(name, coverage), resources, operation);
    }
    #endregion
}
