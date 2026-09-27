using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Controls the bounded resolution of the shared atmospheric radiance lookup.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class AtmosphereSettings
{
    /// <summary>Quality level from zero to three; each step doubles both lookup dimensions.</summary>
    [JsonProperty]
    public int SkyLutQuality { get; set; }

    /// <summary>Azimuth resolution derived from the bounded quality level.</summary>
    internal int LookupWidth => AtmosphereLookup.DefaultWidth << Math.Clamp(SkyLutQuality, 0, 3);

    /// <summary>Elevation resolution derived from the same level to preserve the table's aspect ratio.</summary>
    internal int LookupHeight => AtmosphereLookup.DefaultHeight << Math.Clamp(SkyLutQuality, 0, 3);

    #region Validation
    /// <summary>Bounds the quality level before it controls integration and texture allocation.</summary>
    public void Sanitize()
    {
        SkyLutQuality = Math.Clamp(SkyLutQuality, 0, 3);
    }
    #endregion
}
