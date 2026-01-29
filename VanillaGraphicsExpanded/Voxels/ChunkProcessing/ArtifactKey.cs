namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

internal readonly record struct ArtifactKey(ChunkKey Key, int Version, string ProcessorId);
