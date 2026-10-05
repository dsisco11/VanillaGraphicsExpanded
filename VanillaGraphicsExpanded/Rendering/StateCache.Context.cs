using VanillaGraphicsExpanded.Rendering.Integration;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Associates mutable state knowledge with a registered native context lifetime.</summary>
internal sealed partial class StateCache
{
    private (nint Handle, long Generation) context;
    #region Public API
    /// <summary>Counts successful native fixed-function, dynamic and clear-value transitions.</summary>
    internal long FixedFunctionCalls { get; private set; }
    /// <summary>Returns the observed context generation after withdrawing obsolete knowledge.</summary>
    internal long ContextGeneration { get { SynchronizeContext(); return context.Generation; } }
    #endregion
    #region Private
    /// <summary>A switch discards mutable knowledge; capability lifetime belongs to GpuSupport.</summary>
    private void SynchronizeContext()
    {
        var active = RenderContextRegistry.Current();
        if (context == active && active.Generation != 0) return;
        InvalidateAll();
        context = active;
    }
    #endregion
}
