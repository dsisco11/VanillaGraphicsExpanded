using System.Numerics;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

/// <summary>Rejects accidental CPU traversal when a test expects GPU geometry or deferred lighting only.</summary>
internal sealed class UnexpectedWorldProbeTraceScene:IWorldProbeTraceScene
{
    /// <summary>Fails the admission instead of allowing an unexpected fallback to hide behind valid lighting.</summary>
    public WorldProbeTraceOutcome Trace(Vector3d origin,Vector3 direction,double distance,CancellationToken token,out LumOnWorldProbeTraceHit hit)
    {hit=default;throw new InvalidOperationException("This admission must not traverse CPU geometry.");}
}
