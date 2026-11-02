using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Freezes the inputs admitted for one complete background atmosphere build.</summary>
internal readonly record struct AtmosphereBuildRequest(Vector3 Sun, float Altitude, float Clouds,
    int Width, int Height, int Quality, float GroundAlbedo);
