namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Tracks geometry inputs and atlas publication across frames without retaining GPU height history.</summary>
internal sealed class TerrainDisplacementHistory
{
    private (double X, double Y, double Z, float Focal, int Level, float Pixels, float Start, float End)? previousView;
    private long previousRevision;
    private bool previousComplete;

    #region Frame observations
    /// <summary>Rejects history for changed geometry or atlas publications, including builds completed between frames.</summary>
    internal bool Observe(
        (double X, double Y, double Z, float Focal, int Level, float Pixels, float Start, float End) view,
        long revision, bool complete)
    {
        // Readiness alone loses complete-to-complete replacements. The monotonic revision
        // records intervening publications; incomplete builds remain reactive while streaming.
        bool reactive = previousView != view || previousRevision != revision || !complete || !previousComplete;
        previousView = view;
        previousRevision = revision;
        previousComplete = complete;
        return reactive;
    }

    /// <summary>Forgets the previous world or shader generation so its first frame cannot reuse old geometry.</summary>
    internal void Reset()
    {
        previousView = null;
        previousRevision = 0;
        previousComplete = false;
    }
    #endregion
}
