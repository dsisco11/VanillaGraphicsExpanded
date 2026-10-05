namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Cache-owned entry token retaining a resolved contract until the restoration owner releases it.</summary>
/// <remarks>This token alone is not a disposable restoration adapter and must not wrap production draws.</remarks>
internal sealed class EngineBoundaryScope
{
    internal PipelineStateSnapshot Snapshot { get; }

    #region Public API
    /// <summary>Retains an independently owned snapshot after complete entry resolution.</summary>
    internal EngineBoundaryScope(PipelineStateSnapshot snapshot) { Snapshot = snapshot; }
    #endregion
}
