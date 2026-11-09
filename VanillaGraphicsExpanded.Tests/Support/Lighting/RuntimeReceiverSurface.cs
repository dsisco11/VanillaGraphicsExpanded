using System.Numerics;

namespace VanillaGraphicsExpanded.Tests.Support.Lighting;

/// <summary>Authored engine raster material at a visible receiver, independent from cached source surfaces.</summary>
internal readonly record struct RuntimeReceiverSurface(Vector3 Albedo, float Metallic = 0, float Roughness = 1,
    float Emission = 0, float Transmission = 0);
