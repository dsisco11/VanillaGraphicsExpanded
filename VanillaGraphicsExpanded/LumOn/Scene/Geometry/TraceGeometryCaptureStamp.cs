namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>Tracks source coverage, its publication slot and completed material-table publication.</summary>
internal readonly record struct TraceGeometryCaptureStamp(bool Covered, long Publication, long Tables);
