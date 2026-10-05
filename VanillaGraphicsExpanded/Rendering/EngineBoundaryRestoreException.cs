using System;
namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reports a failed engine handoff; optional-rendering fallback must never swallow this failure.</summary>
internal sealed class EngineBoundaryRestoreException : Exception
{
    #region Public API
    /// <summary>Retains every failure which prevented a safe engine handoff.</summary>
    internal EngineBoundaryRestoreException(string message, Exception cause) : base(message, cause) { }

    /// <summary>Recognizes restoration failures even when combined with an operation error.</summary>
    internal static bool IsRestorationFailure(Exception error)
    {
        if (error is EngineBoundaryRestoreException or Shaders.ShaderOwnershipRestoreException) return true;
        if (error is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions)
                if (IsRestorationFailure(inner)) return true;
        return false;
    }
    #endregion
}
