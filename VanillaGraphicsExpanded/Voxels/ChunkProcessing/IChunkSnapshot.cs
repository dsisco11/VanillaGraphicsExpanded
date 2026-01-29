using System;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public interface IChunkSnapshot : IDisposable
{
    ChunkKey Key { get; }
    int Version { get; }

    int SizeX { get; }
    int SizeY { get; }
    int SizeZ { get; }

    // Element type is system-dependent; use a concrete snapshot type for a chosen voxel representation.
}

