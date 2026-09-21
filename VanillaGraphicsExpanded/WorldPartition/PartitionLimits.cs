using System;

namespace VanillaGraphicsExpanded.WorldPartition;

/// <summary>Separate resource ceilings; capture/dispatch/upload limits reset each pump.</summary>
internal sealed record PartitionLimits(int ResidentCells, int InFlight, int Captures, int Dispatches, long UploadBytes)
{
    /// <summary>Validates nonnegative limits, including intentional zero budgets.</summary>
    public void Validate()
    {
        if (ResidentCells < 0 || InFlight < 0 || Captures < 0 || Dispatches < 0 || UploadBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(ResidentCells));
    }
}
