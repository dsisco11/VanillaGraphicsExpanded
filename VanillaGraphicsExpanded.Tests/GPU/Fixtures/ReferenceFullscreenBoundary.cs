using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Collections.Generic;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Retains historical partial fullscreen boundaries for the independent rendering reference.</summary>
internal static class ReferenceFullscreenBoundary
{
    #region Public API
    /// <summary>Resolves the complete interruption before allocation or drawing, preserving each participating shader's inputs.</summary>
    internal static bool TryRun(string name, IEnumerable<GlPipelineDesc> pipelines,
        IEnumerable<GpuProgram> programs, Action<EngineBoundaryScope> operation)
    {
        OpenGlStateCache.Current.RequireOutsideEngineBoundary();
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
        if (!OpenGlStateCache.Current.TryBeginEngineBoundary(new EngineBoundaryDeclaration(name, coverage), out var boundary, resources)) return false;
        using (boundary) { operation(boundary!); }
        return true;
    }
    #endregion
}
