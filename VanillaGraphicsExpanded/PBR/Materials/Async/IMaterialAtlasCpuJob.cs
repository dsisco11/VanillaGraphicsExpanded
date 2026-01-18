using System.Threading;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// Represents a unit of CPU-only work for building atlas tile data.
/// </summary>
internal interface IMaterialAtlasCpuJob<out TGpuJob>
    where TGpuJob : struct
{
    int GenerationId { get; }

    int AtlasTextureId { get; }

    int Priority { get; }

    TGpuJob Execute(CancellationToken cancellationToken);
}
