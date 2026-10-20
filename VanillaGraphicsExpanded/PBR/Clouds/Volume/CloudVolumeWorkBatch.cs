using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Freezes the exact field, density and physical-source inputs associated with GPU-selected lighting lists.</summary>
internal sealed record CloudVolumeWorkBatch(CloudVolumeFields Fields,AtmosphereLighting Lighting,uint Frame,uint Revision,
    uint FieldRevision,uint DensityRevision,uint DensityFrame,int ActiveSources,int NewlyActive,bool Direct,bool Diffuse,
    GpuIndirectBuffer Commands);
