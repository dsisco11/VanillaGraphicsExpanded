using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Controls the bounded resolution of sky radiance and atmospheric multiple-scattering lookups.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class AtmosphereSettings
{
    /// <summary>Quality level from zero to three; both atmospheric lookups scale by quality plus one.</summary>
    [JsonProperty]
    public AtmosphereQuality SkyLutQuality { get; set; } = AtmosphereQuality.Low;

    /// <summary>Azimuth resolution derived from the bounded quality level.</summary>
    internal int LookupWidth => AtmosphereLookup.DefaultWidth * (Math.Clamp((int)SkyLutQuality, 0, 3) + 1);

    /// <summary>Elevation resolution derived from the same level to preserve the table's aspect ratio.</summary>
    internal int LookupHeight => AtmosphereLookup.DefaultHeight * (Math.Clamp((int)SkyLutQuality, 0, 3) + 1);

    #region Validation
    /// <summary>Bounds the quality level before it controls integration and texture allocation.</summary>
    public void Sanitize()
    {
        SkyLutQuality = (AtmosphereQuality)Math.Clamp((int)SkyLutQuality, 0, 3);
    }
    #endregion
}
