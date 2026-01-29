namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public interface IChunkVersionProvider
{
    int GetCurrentVersion(ChunkKey key);
}

