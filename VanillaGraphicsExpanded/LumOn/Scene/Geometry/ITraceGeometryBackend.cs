using System;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Render-thread storage owned by one shared geometry partition.</summary>
internal interface ITraceGeometryBackend : IDisposable
{
    int Resolution { get; }
    long TablesRevision { get; }
    /// <summary>Retains overlap and clears departing owners before publishing a new window.</summary>
    void SetWindow(TraceGeometryCoverage coverage);
    /// <summary>Uploads a contiguous prerequisite table range; only the final range commits its revision.</summary>
    void UploadTableRange(TraceGeometryTables tables, int offset, int bytes);
    /// <summary>Writes all companions before readiness under a validated coordinator lease.</summary>
    bool Publish(PartitionRequest request, TraceGeometryCell cell, TraceGeometryTables tables);
    /// <summary>Hides content only if the physical slot still belongs to this logical owner.</summary>
    void Invalidate(in PartitionCellKey key);
}
