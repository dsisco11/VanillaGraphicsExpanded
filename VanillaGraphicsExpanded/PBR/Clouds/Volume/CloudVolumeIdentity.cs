namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Names the owning world and density/transport representation independently of transient native frames.</summary>
internal sealed record CloudVolumeIdentity(object Owner, long World, long Representation)
{
    #region Public API
    /// <summary>Rejects replaced owner instances even if their value equality would compare equal.</summary>
    internal bool Matches(CloudVolumeIdentity other) => ReferenceEquals(Owner, other.Owner) &&
        World == other.World && Representation == other.Representation;
    #endregion
}
