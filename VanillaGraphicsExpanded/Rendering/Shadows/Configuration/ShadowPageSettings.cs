using System;
using Newtonsoft.Json;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Configuration;

/// <summary>Configurable depth/table allowances; aggregate shadow scheduling owns the remaining resource budget.</summary>
[JsonObject(MemberSerialization.OptIn)]
public sealed class ShadowPageSettings
{
    [JsonProperty] public int MemoryMiB { get; set; } = 128;
    [JsonProperty] public int PageSize { get; set; } = 128;
    /// <summary>Requested horizontal tile count in each integer bank.</summary>
    [JsonProperty] public int TileColumns { get; set; } = 16;
    /// <summary>Requested vertical tile count in each integer bank.</summary>
    [JsonProperty] public int TileRows { get; set; } = 16;
    /// <summary>Requested resident bank ceiling within the statically bound image/sampler interface.</summary>
    [JsonProperty] public int BankCapacity { get; set; } = 4;
    [JsonProperty] public int ProbeLimit { get; set; } = 128;
    [JsonProperty] public int OperationsPerBatch { get; set; } = 256;
    [JsonProperty] public double PointNear { get; set; } = 0.05;
    [JsonProperty] public double DepthBiasTexels { get; set; } = 0.5;
    [JsonProperty] public double NormalBiasTexels { get; set; } = 0.5;
    [JsonProperty] public double SlopeBiasTexels { get; set; } = 1;

    #region Public API
    /// <summary>Restores invalid fields to finite defaults without promising unsupported aggregate layouts.</summary>
    internal void Sanitize()
    {
        MemoryMiB = Valid(MemoryMiB, 4096, 128); PageSize = Valid(PageSize, 1024, 128);
        if ((PageSize & (PageSize - 1)) != 0) PageSize = 128;
        TileColumns = Valid(TileColumns, 128, 16); TileRows = Valid(TileRows, 128, 16);
        BankCapacity = Valid(BankCapacity, 4, 4);
        ProbeLimit = Valid(ProbeLimit, 8192, 128); OperationsPerBatch = Valid(OperationsPerBatch, 65536, 256);
        if (!double.IsFinite(PointNear) || PointNear <= 0 || PointNear > 1) PointNear = 0.05;
        DepthBiasTexels = Bias(DepthBiasTexels, 0.5); NormalBiasTexels = Bias(NormalBiasTexels, 0.5);
        SlopeBiasTexels = Bias(SlopeBiasTexels, 1);
    }
    /// <summary>Freezes a validated policy so mutable configuration cannot change in-flight addressing.</summary>
    internal ShadowPageSettings Freeze() { var copy = (ShadowPageSettings)MemberwiseClone(); copy.Sanitize(); return copy; }
    #endregion
    #region Private
    /// <summary>Validates one finite positive capacity.</summary>
    private static int Valid(int value, int max, int fallback) => value > 0 && value <= max ? value : fallback;
    /// <summary>Bounds bias to four texels and rejects nonfinite controls.</summary>
    private static double Bias(double value, double fallback) => double.IsFinite(value) && value >= 0 && value <= 4 ? value : fallback;
    #endregion
}
