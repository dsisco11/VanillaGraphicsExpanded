namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Defines separate occupied-origin ray allowances and bounded complete-block refill capacity.</summary>
internal static class CloudVolumeLightingBudget
{
    internal const int RaysPerUnit=2560,DefaultRays=81920,MaximumRays=327680;
    internal const int MaximumDirectJobs=256,MaximumDiffuseJobs=128;
    #region Public API
    /// <summary>Bounds candidate selection effort while reusing the maximum existing list allocation.</summary>
    internal static int Candidates(int rays,bool direct)=>rays/RaysPerUnit*(direct?2:1);
    /// <summary>Rounds down to 512 direct rays and 2048 diffuse rays per throughput unit.</summary>
    internal static int Sanitize(int rays)=>System.Math.Clamp(rays/RaysPerUnit,1,MaximumRays/RaysPerUnit)*RaysPerUnit;
    #endregion
}
