using System.Threading;

using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal interface IWorldProbeTraceScene
{
    /// <summary>
    /// Traces a ray from <paramref name="originWorld"/> in the direction <paramref name="dirWorld"/>.
    /// Returns the first hit distance in world units (block units) if a hit is found.
    /// </summary>
    bool Trace(Vec3d originWorld, Vec3f dirWorld, double maxDistance, CancellationToken cancellationToken, out double hitDistance);
}
