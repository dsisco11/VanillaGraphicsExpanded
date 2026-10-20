using System.Numerics;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Borrows one frozen camera, physical lighting and view-specific medium/depth resource set for synchronous drawing.</summary>
internal sealed record CloudVolumeViewScene(VgeFrameUniformBuffer Camera,GpuShaderStorageBuffer Lighting,
    AtmosphereLighting Atmosphere,GpuTexture MaterialDepth,GpuTexture VisibilityDepth,GpuTexture LiquidDepth,
    DynamicTexture3D Aerial,DynamicTexture3D Attenuation,GpuTexture? Shafts=null,
    float Underwater=0,Vector4 Fog=default,Vector3 FogColor=default);
