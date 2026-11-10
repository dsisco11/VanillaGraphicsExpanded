using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Bounds complete scene preparation and page drawing across all groups and destination arrays.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowDrawSettings
{
    [JsonProperty] public int MemoryMiB { get; set; } = 32;
    [JsonProperty] public int GroupVisits { get; set; } = 4096;
    [JsonProperty] public int RangeVisits { get; set; } = 65536;
    [JsonProperty] public int EntityVisits { get; set; } = 4096;
    [JsonProperty] public int PageCapacity { get; set; } = 128;
    [JsonProperty] public int PairCapacity { get; set; } = 32768;
    [JsonProperty] public int ProbeLimit { get; set; } = 128;
    [JsonProperty] public int ExpansionVisits { get; set; } = 1048576;
    [JsonProperty] public int CommandSlots { get; set; } = 65536;
    [JsonProperty] public int PageInstances { get; set; } = 32768;
    [JsonProperty] public int EstimatedTriangles { get; set; } = 8388608;

    #region Public API
    /// <summary>Restores invalid controls to finite defaults; allocation still validates aggregate sizes.</summary>
    internal void Sanitize()
    {
        MemoryMiB = Valid(MemoryMiB, 4096, 32);
        GroupVisits = Valid(GroupVisits, 65536, 4096);
        RangeVisits = Valid(RangeVisits, 1048576, 65536);
        EntityVisits = Valid(EntityVisits, 65536, 4096);
        PageCapacity = Valid(PageCapacity, 8192, 128);
        PairCapacity = Valid(PairCapacity, 1048576, 32768);
        if ((PairCapacity & (PairCapacity - 1)) != 0) PairCapacity = 32768;
        ProbeLimit = Valid(ProbeLimit, 65536, 128);
        ExpansionVisits = Valid(ExpansionVisits, 16777216, 1048576);
        CommandSlots = Valid(CommandSlots, 1048576, 65536);
        PageInstances = Valid(PageInstances, 1048576, 32768);
        EstimatedTriangles = Valid(EstimatedTriangles, int.MaxValue, 8388608);
    }

    /// <summary>Separates in-flight policy from mutable live configuration.</summary>
    internal ShadowDrawSettings Freeze()
    {
        var result = (ShadowDrawSettings)MemberwiseClone();
        result.Sanitize();
        return result;
    }
    #endregion

    #region Private
    /// <summary>Bounds user input before checked layout arithmetic.</summary>
    private static int Valid(int value, int maximum, int fallback) => value > 0 && value <= maximum ? value : fallback;
    #endregion
}
