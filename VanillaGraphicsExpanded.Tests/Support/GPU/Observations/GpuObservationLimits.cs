namespace VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

/// <summary>Establishes exact unsigned counter and source-coordinate bounds before native dispatch.</summary>
internal static class GpuObservationLimits
{
    #region Internal API
    /// <summary>Reserves uint.MaxValue as the missing coordinate and bounds four scalar addresses per texel.</summary>
    internal static int RequireDomain(int width, int height, int depth)
    {
        if (width <= 0 || height <= 0 || depth <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        long count = checked((long)width * height * depth);
        if (count > uint.MaxValue / 4) throw new ArgumentOutOfRangeException(nameof(width), "Observation scalar coordinates exceed their exact counter domain.");
        return (int)count;
    }

    /// <summary>Uses overflow-free ceiling division for a nonempty bounded invocation domain.</summary>
    internal static int Groups(int records)
    {
        if (records <= 0 || records > uint.MaxValue / 4) throw new ArgumentOutOfRangeException(nameof(records));
        return (records - 1) / 64 + 1;
    }
    #endregion
}
