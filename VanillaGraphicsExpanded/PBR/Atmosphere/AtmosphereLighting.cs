using System;
using System.Collections.Immutable;
using System.Numerics;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>A coherent sky lookup and its hemispherical lighting integrals, expressed in scene-linear units.</summary>
internal sealed record AtmosphereLighting(Vector3 Sun, Vector3 Solar, Vector3 Environment, Vector3 Horizon, Vector3 Extinction, ImmutableArray<float> Sky)
{
    /// <summary>Identifies the extended coherent solar/lunar/background publication.</summary>
    internal bool HasNightSources { get; init; }
    /// <summary>Independent lunar direction; never inferred from the solar direction.</summary>
    internal Vector3 Moon { get; init; } = Vector3.UnitY;
    /// <summary>Extinguished lunar direct illuminance in lux.</summary>
    internal Vector3 Lunar { get; init; }
    /// <summary>Frozen eight-step lunar phase shared by disk appearance and incident power.</summary>
    internal float MoonPhase { get; init; }
    /// <summary>Equivalent luminous lunar disk radius, matching the installed sprite footprint.</summary>
    internal float MoonAngularRadius { get; init; } = .03252f;
    /// <summary>Unweighted lunar Mie transport for the sky lookup.</summary>
    internal ImmutableArray<float> SkyMoonMie { get; init; } = ImmutableArray<float>.Empty;
    /// <summary>Unweighted lunar Mie transport for finite aerial paths.</summary>
    internal ImmutableArray<float> AerialMoonMie { get; init; } = ImmutableArray<float>.Empty;
    /// <summary>The sun owns direct lighting while visible; otherwise an illuminated visible moon takes over.</summary>
    internal bool MoonIsDirect => HasNightSources
        && AtmosphereSolarDisk.Visibility(MathF.Asin(Math.Clamp(Sun.Y, -1, 1)), HorizonElevation) <= 0
        && Lunar != Vector3.Zero;
    /// <summary>Direction of the single direct emitter sharing the existing shadow map.</summary>
    internal Vector3 DirectDirection => MoonIsDirect ? Moon : Sun;
    /// <summary>Selected direct illuminance; other celestial contributions remain in the sky environment.</summary>
    internal Vector3 DirectIrradiance => MoonIsDirect ? Lunar : Solar;
    /// <summary>Radius of the selected finite disk; the larger lunar disk retains conservative solar BRDF bounds.</summary>
    internal float DirectAngularRadius => MoonIsDirect ? MoonAngularRadius : AtmosphereSolarDisk.AngularRadius;
    internal ImmutableArray<float> SkyMie { get; init; } = ImmutableArray<float>.Empty;
    internal ImmutableArray<float> AerialMie { get; init; } = ImmutableArray<float>.Empty;
    internal int Width { get; init; } = AtmosphereLookup.DefaultWidth;
    internal int Height { get; init; } = AtmosphereLookup.DefaultHeight;
    internal float HorizonElevation { get; init; }
    internal float Altitude { get; init; } = .001f;
    /// <summary>Cloud-cover bucket admitted with this completed atmosphere, used for matching external extinction.</summary>
    internal float CloudCoverage { get; init; }
    /// <summary>Separately budgeted ground-level airglow embedded in sky and finite-path lighting.</summary>
    internal float BackgroundRadiance { get; init; }
    /// <summary>Complete source identity used by visible and incident celestial lighting; null means airglow only.</summary>
    internal Celestial.CelestialSource? CelestialSource { get; init; }
    /// <summary>Frozen celestial-to-world rotation shared by this publication's display and diffuse integral.</summary>
    internal Matrix4x4 CelestialRotation { get; init; } = Matrix4x4.Identity;
    /// <summary>Attenuated horizontal celestial illuminance divided by pi, excluding airglow and lunar transport.</summary>
    internal Vector3 CelestialEnvironment { get; init; }
    internal ImmutableArray<float> AerialRadiance { get; init; } = ImmutableArray<float>.Empty;
    internal ImmutableArray<float> AerialAttenuation { get; init; } = ImmutableArray<float>.Empty;
    /// <summary>Dimensionless observer-to-space transmission by elevation, completed with this atmospheric generation.</summary>
    internal ImmutableArray<float> ExternalTransmission { get; init; } = ImmutableArray<float>.Empty;
    /// <summary>Encoding of this generation's light textures; immutable CPU quantities remain unscaled.</summary>
    internal float LookupStorageScale { get; init; } = 1;
    /// <summary>Physical generations carry their calibration identity; zero retains the legacy scene-unit publication.</summary>
    internal uint CalibrationGeneration { get; init; }
    /// <summary>Maximum reconstructed sky/aerial RGB from the admitted uploaded bands, in physical nits.</summary>
    internal float ReconstructionMaximumNits { get; init; }
}

