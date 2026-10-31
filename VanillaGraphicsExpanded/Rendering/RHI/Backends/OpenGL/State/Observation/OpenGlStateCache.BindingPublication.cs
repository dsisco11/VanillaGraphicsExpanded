namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Expires binding-publication receipts when the existing executed knowledge can change.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Changes on binding mutation, uncertainty, restoration or originating-context reset.</summary>
    internal ulong BindingPublicationRevision { get; private set; }
    /// <summary>Distinguishes external callbacks during publication from its own controlled binding changes.</summary>
    internal ulong BindingObservationRevision { get; private set; }

    /// <summary>Expires receipts without discarding any slot knowledge or resource metadata.</summary>
    internal void ChangeBindingPublication() => BindingPublicationRevision++;

    /// <summary>Conservatively expires receipts on observed external work, including callback reentry.</summary>
    internal void ObserveBindingPublication()
    {
        BindingObservationRevision++;
        ChangeBindingPublication();
    }
    #endregion
}
