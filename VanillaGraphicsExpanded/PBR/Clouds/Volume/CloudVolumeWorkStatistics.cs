namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Retains fenced ray reservations and origin charges without another runtime readback.</summary>
internal readonly record struct CloudVolumeWorkStatistics(uint DirectAllowance,uint DiffuseAllowance,
    uint DirectRays,uint DiffuseRays,uint UnknownDirectJobs,uint UnknownDiffuseJobs,uint ActiveSolvers,uint EmptyDiffuseOrigins);
