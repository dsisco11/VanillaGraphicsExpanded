using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.Integration;

/// <summary>Attempts independent rendering cleanup actions before reporting their combined failures.</summary>
internal static class RenderingCleanup
{
    #region Public API
    /// <summary>Preserves retirement order while allowing later owners to release after an earlier failure.</summary>
    internal static void Run(params Action[] actions)
    {
        List<Exception> failures = [];
        // Each action owns its own references; one failed release cannot strand its siblings.
        foreach (var action in actions)
        {
            try { action(); }
            catch (Exception failure) { failures.Add(failure); }
        }
        if (failures.Count != 0) throw new AggregateException("Rendering cleanup failed.", failures);
    }
    #endregion
}
