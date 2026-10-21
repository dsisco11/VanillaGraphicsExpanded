using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Describes one coherent drawable publication without exposing mutable solver scratch.</summary>
internal sealed record CloudVolumeViewFields(CloudVolumeDomain Domain,CloudVolumeEncoding Encoding,
    CloudVolumeLightingBounds? LightingBounds,uint Revision,GpuTexture Density,GpuTexture Direct,GpuTexture Diffuse,
    GpuShaderStorageBuffer Regions,GpuShaderStorageBuffer Occupancy,object? MaterialIdentity=null);
