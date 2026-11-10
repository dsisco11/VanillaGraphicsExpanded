using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Bounds native shadow input storage and work independently of illumination-list capacity.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowInputSettings
{
    /// <summary>Maximum loaded coordinates; overflow withdraws the whole domain publication.</summary>
    [JsonProperty] public int ChunkCapacity { get; set; } = 65536;
    /// <summary>Maximum native range descriptions in one frozen batch.</summary>
    [JsonProperty] public int RangeCapacity { get; set; } = 524288;
    /// <summary>Maximum distinct queued coordinates before a bounded native rebootstrap is required.</summary>
    [JsonProperty] public int ChangeCapacity { get; set; } = 8192;
    /// <summary>Maximum collision probes per coordinate; exhaustion never aliases another chunk.</summary>
    [JsonProperty] public int ProbeLimit { get; set; } = 128;

    #region Public API
    /// <summary>Restores invalid values to finite defaults before the owner freezes a policy.</summary>
    internal void Sanitize()
    {
        if (ChunkCapacity is <= 0 or > 1048576) ChunkCapacity = 65536;
        if (RangeCapacity is <= 0 or > 8388608) RangeCapacity = 524288;
        if (ChangeCapacity is <= 0 or > 1048576) ChangeCapacity = 8192;
        if (ProbeLimit is <= 0 or > 4096) ProbeLimit = 128;
    }
    #endregion
}
