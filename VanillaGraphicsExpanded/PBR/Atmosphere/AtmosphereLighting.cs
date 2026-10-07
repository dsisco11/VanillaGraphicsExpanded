using System.Collections.Immutable;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>A coherent sky lookup and its hemispherical lighting integrals, expressed in scene-linear units.</summary>
internal sealed record AtmosphereLighting(Vector3 Sun, Vector3 Solar, Vector3 Environment, Vector3 Horizon, Vector3 Extinction, ImmutableArray<float> Sky)
{
    internal ImmutableArray<float> SkyMie { get; init; } = ImmutableArray<float>.Empty;
    internal ImmutableArray<float> AerialMie { get; init; } = ImmutableArray<float>.Empty;
    internal int Width { get; init; } = AtmosphereLookup.DefaultWidth;
    internal int Height { get; init; } = AtmosphereLookup.DefaultHeight;
    internal float HorizonElevation { get; init; }
    internal float Altitude { get; init; } = .001f;
    internal ImmutableArray<float> AerialRadiance { get; init; } = ImmutableArray<float>.Empty;
    internal ImmutableArray<float> AerialAttenuation { get; init; } = ImmutableArray<float>.Empty;
}

