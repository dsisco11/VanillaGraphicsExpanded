using System;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Demand;

/// <summary>Participating surface families whose complete early bounds must precede the shadow gate.</summary>
[Flags]
internal enum ShadowSurfaceCategory { None = 0, Opaque = 1, Cutout = 2, Entities = 4, Foliage = 8, Water = 16, Transparent = 32, All = 63 }
