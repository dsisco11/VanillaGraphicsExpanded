namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>Counter offsets shared with surface_work_diagnostics.glsl; texel and ray units remain separate.</summary>
internal enum SurfaceWorkCounter
{
    Texels, Completed, Unchanged, Empty, Hidden, OriginOutside, OriginUnpublished,
    OriginUnsupported, Unseeded, Nonfinite, Rays, Hit, Sky, Outside, Unpublished,
    Unsupported, Budget, Distance, HitMaterial, HitLighting, CaptureOutside,
    CaptureUnpublished, CaptureMaterial, Count
}
