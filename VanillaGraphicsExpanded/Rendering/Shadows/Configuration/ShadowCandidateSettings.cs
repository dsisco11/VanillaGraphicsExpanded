using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Bounds association storage and aggregate GPU work; values are provisional tuning controls.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowCandidateSettings
{
    [JsonProperty] public int MemoryMiB { get; set; } = 16;
    [JsonProperty] public int HeaderCapacity { get; set; } = 32;
    [JsonProperty] public int SegmentsPerAssociation { get; set; } = 64;
    [JsonProperty] public int DedupCapacity { get; set; } = 8192;
    [JsonProperty] public int ProbeLimit { get; set; } = 128;
    [JsonProperty] public int ChangeCapacity { get; set; } = 8192;
    [JsonProperty] public int SlabsPerSlice { get; set; } = 64;
    [JsonProperty] public int CellsPerSlice { get; set; } = 4096;
    [JsonProperty] public int ProbesPerSlice { get; set; } = 32768;
    [JsonProperty] public int ChangesPerSlice { get; set; } = 256;
    [JsonProperty] public int SlabsPerBatch { get; set; } = 1024;
    [JsonProperty] public int CellsPerBatch { get; set; } = 65536;
    [JsonProperty] public int ProbesPerBatch { get; set; } = 524288;
    [JsonProperty] public int ChangesPerBatch { get; set; } = 8192;
    [JsonProperty] public double RefreshGuardFraction { get; set; } = 0.25;

    #region Public API
    /// <summary>Materializes finite settings; incompatible aggregate layouts remain explicit allocation failures.</summary>
    internal void Sanitize()
    {
        MemoryMiB = Valid(MemoryMiB, 4096, 16); HeaderCapacity = Valid(HeaderCapacity, 4096, 32);
        SegmentsPerAssociation = Valid(SegmentsPerAssociation, 16384, 64);
        DedupCapacity = Valid(DedupCapacity, 4194304, 8192);
        if ((DedupCapacity & (DedupCapacity - 1)) != 0) DedupCapacity = 8192;
        ProbeLimit = Valid(ProbeLimit, 65536, 128); ChangeCapacity = Valid(ChangeCapacity, 1048576, 8192);
        SlabsPerSlice = Valid(SlabsPerSlice, 1048576, 64); CellsPerSlice = Valid(CellsPerSlice, 16777216, 4096);
        ProbesPerSlice = Valid(ProbesPerSlice, 16777216, 32768); ChangesPerSlice = Valid(ChangesPerSlice, 1048576, 256);
        SlabsPerBatch = Valid(SlabsPerBatch, 16777216, 1024); CellsPerBatch = Valid(CellsPerBatch, 16777216, 65536);
        ProbesPerBatch = Valid(ProbesPerBatch, 16777216, 524288); ChangesPerBatch = Valid(ChangesPerBatch, 16777216, 8192);
        if (!double.IsFinite(RefreshGuardFraction) || RefreshGuardFraction <= 0 || RefreshGuardFraction >= 1) RefreshGuardFraction = 0.25;
    }

    /// <summary>Freezes a sanitized copy so later configuration edits cannot mutate an in-flight allocator.</summary>
    internal ShadowCandidateSettings Freeze()
    {
        var result = (ShadowCandidateSettings)MemberwiseClone(); result.Sanitize(); return result;
    }
    #endregion

    #region Private
    /// <summary>Restores invalid integer controls to the documented finite defaults.</summary>
    private static int Valid(int value, int max, int fallback) => value > 0 && value <= max ? value : fallback;
    #endregion
}
