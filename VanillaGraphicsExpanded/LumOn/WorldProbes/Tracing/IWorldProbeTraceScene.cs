using System.Threading;
using System.Numerics;

using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

internal interface IWorldProbeTraceScene
{
    /// <summary>
    /// Traces a ray from <paramref name="originWorld"/> in the direction <paramref name="dirWorld"/>.
    /// Returns a hit record if a hit is found.
    /// </summary>
    bool Trace(Vec3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit);
}
