namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public enum ChunkWorkStatus
{
    Success = 0,
    Superseded = 1,
    Canceled = 2,
    Failed = 3,
    ChunkUnavailable = 4,
}

public enum ChunkWorkError
{
    None = 0,
    SnapshotFailed = 1,
    ProcessorFailed = 2,
    Unknown = 3,
}

public readonly record struct ChunkWorkResult<TArtifact>(
    ChunkWorkStatus Status,
    ChunkKey Key,
    int RequestedVersion,
    string ProcessorId,
    TArtifact? Artifact = default,
    ChunkWorkError Error = ChunkWorkError.None,
    string? Reason = null);

