using VanillaGraphicsExpanded.LumOn.Scene.Geometry;

namespace VanillaGraphicsExpanded.LumOn.Scene.Fallback;

/// <summary>Rejects delayed answers after world edits, geometry invalidation or lighting resource replacement.</summary>
internal sealed record SurfaceFallbackLifetime(TraceGeometryGpuScene Scene, long Invalidation, long Lighting, long World);

/// <summary>Retains one captured physical page's exact virtual ownership and capture lifetime.</summary>
internal readonly record struct SurfaceFallbackPage(uint Page, ulong Key, ushort Generation, long CaptureRevision);
