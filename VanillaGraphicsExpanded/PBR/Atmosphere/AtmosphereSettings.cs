using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.PBR.Atmosphere;

/// <summary>Controls bounded atmospheric lookup detail and generated celestial source resolution.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class AtmosphereSettings
{
    /// <summary>Global sky quality from zero to three; atmospheric lookups and celestial resources share this selector.</summary>
    [JsonProperty]
    public int SkyLutQuality { get; set; } = 2;

    /// <summary>Azimuth resolution derived from the bounded quality level.</summary>
    internal int LookupWidth => AtmosphereLookup.DefaultWidth * (Math.Clamp(SkyLutQuality, 0, 3) + 1);

    /// <summary>Elevation resolution derived from the same level to preserve the table's aspect ratio.</summary>
    internal int LookupHeight => AtmosphereLookup.DefaultHeight * (Math.Clamp(SkyLutQuality, 0, 3) + 1);

    #region Validation
    /// <summary>Bounds the quality level before it controls integration and texture allocation.</summary>
    public void Sanitize()
    {
        SkyLutQuality = Math.Clamp(SkyLutQuality, 0, 3);
    }
    #endregion
}
