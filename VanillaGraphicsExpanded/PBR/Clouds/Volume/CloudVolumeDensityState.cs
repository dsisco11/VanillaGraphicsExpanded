namespace VanillaGraphicsExpanded.PBR.Clouds.Volume;

/// <summary>Retains descriptor-window and density-completion facts without retaining a native texture borrow.</summary>
internal sealed class CloudVolumeDensityState
{
    internal bool SnapshotValid { get; set; }
    internal bool InitialComplete { get; set; }
    internal long WindowX { get; set; }
    internal long WindowZ { get; set; }
    internal uint Revision { get; set; }
    internal uint SnapshotFrame { get; set; }
    internal uint CompletedFrame { get; set; }
    internal int Remaining { get; set; }
    internal int Selected { get; set; }
    /// <summary>Counts blocks whose newly encoded extinction actually differs from stored values.</summary>
    internal int Changed { get; set; }
    /// <summary>Tracks observations and backlog that still require a GPU selection/maintenance pass.</summary>
    internal bool MaintenanceRequired { get; set; } = true;
    /// <summary>Retains domain/guard changes whose maintenance writes precede generation comparison.</summary>
    internal bool LayoutChanged { get; set; }
    internal long NativeGeneration { get; set; }
    internal long NativeUpload { get; set; }
    internal long NativeAllocation { get; set; }
}
