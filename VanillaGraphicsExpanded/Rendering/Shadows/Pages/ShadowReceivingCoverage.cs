using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Pages;

/// <summary>Records depth-inclusive receiver/filter/bias coverage independently of virtual page identity.</summary>
internal readonly record struct ShadowReceivingCoverage(ShadowBounds Region, long SourceLifetime,
    long Projection, long World, long SceneSequence, long SceneEpoch, long DepthGeneration)
{
    #region Public API
    /// <summary>Requires matching dependencies and complete applied changes before old depths serve a receiver.</summary>
    internal bool Covers(ShadowBounds request, long sourceLifetime, long projection, long world,
        ShadowChangeBatch changes, long appliedThrough) => DepthGeneration > 0 && SourceLifetime > 0
        && Projection > 0 && World > 0 && SceneEpoch > 0 && SceneSequence >= 0 && Region.Contains(request)
        && SourceLifetime == sourceLifetime && Projection == projection && World == world
        && changes.Complete && !changes.RequiresBroadInvalidation && SceneEpoch == changes.Epoch
        && appliedThrough >= changes.Sequence && SceneSequence <= appliedThrough;
    #endregion
}
