using VanillaGraphicsExpanded.Rendering.Integration;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Associates state knowledge and capabilities with a registered native context lifetime.</summary>
internal sealed partial class StateCache
{
    private (nint Handle, long Generation) context;
    #region Public API
    /// <summary>Counts successful native fixed-function, dynamic and clear-value transitions.</summary>
    internal long FixedFunctionCalls { get; private set; }
    /// <summary>Counts immutable capability resolutions, retained across context lifetimes.</summary>
    internal long CapabilityQueries { get; private set; }
    /// <summary>Returns the observed context generation after withdrawing obsolete knowledge.</summary>
    internal long ContextGeneration { get { SynchronizeContext(); return context.Generation; } }
    #endregion
    #region Private
    /// <summary>A switch discards knowledge and capabilities; returning never revives the former cache.</summary>
    private void SynchronizeContext()
    {
        var active = RenderContextRegistry.Current();
        if (context == active && active.Generation != 0) return;
        InvalidateAll();
        maxDrawBuffers = 0;
        maxPatchVertices = 0;
        maxViewportWidth = maxViewportHeight = 0;
        storageBufferOffsetAlignment = null;
        context = active;
    }
    #endregion
}
