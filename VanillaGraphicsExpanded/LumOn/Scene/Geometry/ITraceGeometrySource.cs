using System;
using VanillaGraphicsExpanded.Voxels.ChunkProcessing;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Supplies versioned chunk snapshots to shared geometry while owning their world lifetime.</summary>
internal interface ITraceGeometrySource : IDisposable
{
    /// <summary>Provides coherent source snapshots and their current availability.</summary>
    TraceGeometrySourceCache Cache { get; }

    /// <summary>Updates loaded identities and pending edits for the requested coverage.</summary>
    void Prepare(TraceGeometryCoverage coverage);

    /// <summary>Invalidates a chunk after a world edit notification.</summary>
    void MarkDirty(ChunkKey key);
}
