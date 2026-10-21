namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Defines bounded lighting throughput in complete physical-block ray units.</summary>
internal static class CloudVolumeLightingBudget
{
    internal const int RaysPerUnit=2560,DefaultRays=81920,MaximumRays=327680;
    internal const int MaximumDirectJobs=256,MaximumDiffuseJobs=128;
    #region Public API
    /// <summary>Rounds down to two direct block-source jobs plus one eight-direction diffuse block.</summary>
    internal static int Sanitize(int rays)=>System.Math.Clamp(rays/RaysPerUnit,1,MaximumRays/RaysPerUnit)*RaysPerUnit;
    #endregion
}
