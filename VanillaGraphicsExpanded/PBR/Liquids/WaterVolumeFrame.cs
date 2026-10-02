using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Publishes one completed oriented-boundary capture and its authoritative camera medium.</summary>
internal readonly record struct WaterVolumeFrame(DynamicTexture2D OpticalDepth, DynamicTexture2D Source, WaterMedium? CameraMedium,
    Vector3 CameraScatteringSource = default);
