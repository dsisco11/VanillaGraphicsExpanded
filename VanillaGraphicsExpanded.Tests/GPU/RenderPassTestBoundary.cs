using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Runs pass fixtures under the same complete state ownership required by production submission.</summary>
internal static class RenderPassTestBoundary
{
    #region Public API
    /// <summary>Captures all affected fields and restores them even when setup or fixture assertions throw.</summary>
    internal static void Run(Action<RHICommandContext> operation)
    {
        var commands = RHICommandContext.Current;
        using var boundary = commands.EnterBoundary(new("Tests.RenderPass", true, operations: BoundaryOperations.RenderPasses));
        boundary.Run(() => operation(commands));
    }
    #endregion
}
