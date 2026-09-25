namespace VanillaGraphicsExpanded.LumOn.Scene.HitLighting;

/// <summary>One observed hit-page owner and its last completed lighting publication.</summary>
internal readonly record struct SurfaceHitDependency(uint Page, ulong Key, ushort Generation, long CaptureRevision,
    bool Captured, long Publication)
{
    /// <summary>Compares captured surface identity independently of ordinary lighting progress.</summary>
    public bool SameIdentity(in SurfaceHitDependency other) => Captured && other.Captured && Page == other.Page &&
        Key == other.Key && Generation == other.Generation && CaptureRevision == other.CaptureRevision;
}
