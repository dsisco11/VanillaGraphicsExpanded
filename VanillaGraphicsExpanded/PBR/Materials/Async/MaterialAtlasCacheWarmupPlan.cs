using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed record class MaterialAtlasCacheWarmupPlan(
    IReadOnlyList<IMaterialAtlasCpuJob<MaterialAtlasParamsGpuTileUpload>> MaterialParamsCpuJobs,
    IReadOnlyList<IMaterialAtlasCpuJob<MaterialAtlasNormalDepthGpuJob>> NormalDepthCpuJobs,
    int MaterialParamsPlanned,
    int NormalDepthPlanned)
{
    public DateTime CreatedUtc { get; } = DateTime.UtcNow;
}
