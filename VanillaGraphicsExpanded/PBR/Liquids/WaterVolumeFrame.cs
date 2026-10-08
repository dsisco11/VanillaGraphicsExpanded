using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Publishes one completed oriented-boundary capture and its authoritative camera medium.</summary>
internal readonly record struct WaterVolumeFrame(Texture3D Transport, WaterMedium? CameraMedium,
    Vector3 CameraScatteringSource = default)
{
    /// <summary>Identifies optical transport and scattering-source images within the shared array.</summary>
    public const int OpticalLayer = 0, SourceLayer = 1;
}
