namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Identifies the exact native generation summarized by a hierarchy, without extending its borrow.</summary>
internal readonly record struct CloudHierarchySource(CloudMapInput Input)
{
    #region Public API
    /// <summary>Matches a freshly revalidated map, including frame, owner identity, storage and placement.</summary>
    /// <remarks>Callers obtain current from the native publication immediately before use. This comparison
    /// does not prove completed GPU reduction; the hierarchy resource owner must certify that separately.</remarks>
    internal bool Matches(CloudMapInput current)
    {
        // Reference identity prevents custom owner equality from joining different native lifetimes.
        // Whole-descriptor equality includes upload/shader epochs even when GL names are reused.
        return Input.Owner is not null && ReferenceEquals(Input.Owner, current.Owner)
            && Input.Storage.IsSupported && Input.Placement.IsFinite && Input == current;
    }
    #endregion
}
