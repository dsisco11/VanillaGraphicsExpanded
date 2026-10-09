namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Maps native SSAO quality to fixed upper bounds without a second enable setting.</summary>
internal readonly record struct AmbientOcclusionQuality(int Divisor, int Directions, int Steps)
{
    #region Public API
    /// <summary>Uses half-resolution sampling with denser angular/radial integration at native high quality.</summary>
    internal static AmbientOcclusionQuality FromNative(int quality) => quality >= 2 ? new(2, 6, 6) : new(2, 3, 4);
    #endregion
}
