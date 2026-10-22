namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Selects the user-facing trace resolution independently of the internal dimension divisor.</summary>
public enum CloudTraceResolution
{
    /// <summary>Traces at one quarter of each view dimension.</summary>
    Quarter = 1,
    /// <summary>Traces at one half of each view dimension.</summary>
    Half = 2,
    /// <summary>Traces at full view dimensions without temporal reconstruction.</summary>
    Full = 3
}
