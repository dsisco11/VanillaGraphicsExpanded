using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Runs pass fixtures under the same complete state ownership required by production submission.</summary>
internal static class RenderPassTestBoundary
{
    #region Public API
    /// <summary>Captures all affected fields and restores them even when setup or fixture assertions throw.</summary>
    internal static void Run(Action<EngineBoundaryScope> operation)
    {
        var cache = StateCache.Current;
        var coverage = new PipelineStateCoverage(DepthStateKnowledge.All, RasterizerStateKnowledge.All,
            PrimitiveAssemblyStateKnowledge.All, DynamicDrawStateKnowledge.All, BlendStateKnowledge.All,
            clearColor: true, completeGraphics: true);
        Assert.True(cache.TryBeginEngineBoundary(new EngineBoundaryDeclaration("RenderPassTest", coverage), out var boundary),
            cache.BoundaryEntryFailure?.ToString());
        using (boundary!) boundary!.Run(() => operation(boundary));
    }
    #endregion
}
