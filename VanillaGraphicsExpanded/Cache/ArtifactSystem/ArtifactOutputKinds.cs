using System;

namespace VanillaGraphicsExpanded.Cache.Artifacts;

[Flags]
internal enum ArtifactOutputKinds
{
    None = 0,
    Disk = 1 << 0,
    Gpu = 1 << 1,
}
