using System;

namespace VanillaGraphicsExpanded.PBR.Materials.Cache;

internal interface IMaterialAtlasDiskCacheIndexFlushPolicy
{
    bool FlushSynchronously { get; }

    TimeSpan DebounceDelay { get; }
}
