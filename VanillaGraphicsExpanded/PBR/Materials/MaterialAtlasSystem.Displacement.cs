namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>Publishes coherent native displacement inputs to geometry consumers.</summary>
internal sealed partial class MaterialAtlasSystem
{
    #region Public API
    /// <summary>Freezes either the current authored texture trio or the existing no-displacement policy.</summary>
    internal bool TryCaptureDisplacement(int atlasTextureId, out MaterialDisplacementSnapshot snapshot)
    {
        snapshot = default;
        if (isDisposed || !IsBuildComplete) return false;
        long revision = SurfaceDetailRevision;
        if (!textureStore.TryGetDisplacementTextures(atlasTextureId, out var displacement))
        {
            snapshot = new(revision, default, default, default);
            return true;
        }
        if (!textureStore.TryGetPageTextures(atlasTextureId, out var page) || page.NormalDepthTexture is not { IsValid: true } height)
            return false;
        snapshot = new(revision, displacement!.Indices, displacement.Records, height);
        return IsDisplacementCurrent(snapshot);
    }

    /// <summary>Rejects rebuilds and withdrawn storage before borrowed geometry is submitted.</summary>
    internal bool IsDisplacementCurrent(MaterialDisplacementSnapshot snapshot) =>
        !isDisposed && IsBuildComplete && snapshot.Revision == SurfaceDetailRevision &&
        (!snapshot.HasDisplacement || (snapshot.Indices.IsValid && snapshot.Records.IsValid && snapshot.Height.IsValid));
    #endregion
}
