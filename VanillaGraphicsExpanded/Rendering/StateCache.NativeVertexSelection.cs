namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Observes native VAO selectors without treating selection as a configuration change.</summary>
internal sealed partial class StateCache
{
    #region Internal API
    /// <summary>Withdraws the selector before forwarding an exact native overload.</summary>
    internal static NativeVertexSelection BeginNativeVertexSelection()
    {
        var cache = Current;
        cache.currentVao = null;
        return new(cache);
    }
    #endregion

    /// <summary>Leaves successful and rejected selectors available for ordinary unknown-state recovery.</summary>
    internal readonly struct NativeVertexSelection(StateCache cache)
    {
        #region Internal API
        /// <summary>Discards assumptions without publishing resource metadata or consuming native errors.</summary>
        internal void Complete(bool returned) => cache.currentVao = null;
        #endregion
    }
}
