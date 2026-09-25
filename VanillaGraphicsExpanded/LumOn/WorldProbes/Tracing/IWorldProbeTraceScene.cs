using System.Threading;
using System.Numerics;

using Vintagestory.API.MathTools;

using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Provides geometry traversal with explicit completion and availability outcomes.</summary>
internal interface IWorldProbeTraceScene
{
    /// <summary>
    /// Traces a ray from <paramref name="originWorld"/> in the direction <paramref name="dirWorld"/>.
    /// Returns a hit record for Hit. Only Sky establishes environment visibility;
    /// DistanceLimit proves a finite clear segment and requires distant-light completion.
    /// </summary>
    WorldProbeTraceOutcome Trace(Vector3d originWorld, Vector3 dirWorld, double maxDistance, CancellationToken cancellationToken, out LumOnWorldProbeTraceHit hit);
}
