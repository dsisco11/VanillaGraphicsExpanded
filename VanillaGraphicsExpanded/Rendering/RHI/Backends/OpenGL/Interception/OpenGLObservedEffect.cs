namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Classifies a completed native operation without consuming its native error state.</summary>
internal enum OpenGLObservedEffect
{
    /// <summary>Established command rules and context facts prove the affected value did not change.</summary>
    Rejected,
    /// <summary>The command establishes the supplied effect in the originating context.</summary>
    Changed,
    /// <summary>Available facts cannot certify the affected value; its knowledge must be withdrawn.</summary>
    Uncertain
}
