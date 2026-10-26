using System;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Preserves original context identity for native resource use and cleanup.</summary>
public abstract partial class GpuResource
{
    #region Internal API
    /// <summary>Captures registered identity, or the physical context for an explicitly local buffer tool.</summary>
    internal static unsafe (nint Handle, long Generation) CaptureCreationContext(GpuResourceKind kind)
    {
        var context = RenderContextRegistry.Current();
        return context == default && kind == GpuResourceKind.Buffer ? ((nint)GLFW.GetCurrentContext(), 0) : context;
    }

    /// <summary>Rejects resource use in a foreign namespace without native metadata discovery.</summary>
    internal void RequireOriginalContext()
    {
        if (!IsCurrentResourceContext(nativeContext))
            throw new InvalidOperationException("The resource's original context is required.");
    }

    /// <summary>Compares local physical identity or registered lifetime identity without querying the GPU.</summary>
    internal static unsafe bool IsCurrentResourceContext((nint Handle, long Generation) origin)
        => origin == default || (origin.Generation == 0
            ? origin.Handle == (nint)GLFW.GetCurrentContext()
            : origin == RenderContextRegistry.Current() && RenderContextRegistry.IsAlive(origin));
    #endregion
}
