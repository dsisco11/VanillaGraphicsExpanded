namespace VanillaGraphicsExpanded.Rendering.Integration.Shadows;

/// <summary>Retains required and uploaded revisions independently for native center and edge replacements.</summary>
internal sealed class NativeShadowContentState
{
    internal long RequiredCenter, RequiredEdge, UploadedCenter, UploadedEdge;
    /// <summary>An older replacement remains unresolved even after a newer upload had completed earlier.</summary>
    internal bool Ready => RequiredCenter == UploadedCenter && RequiredEdge == UploadedEdge
        && UploadedCenter >= 0 && UploadedEdge >= 0;
}
