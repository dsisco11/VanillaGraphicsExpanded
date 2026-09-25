using System;
using System.Collections.Immutable;
using System.Numerics;

namespace VanillaGraphicsExpanded.LumOn.Scene.Fallback;

/// <summary>Builds complete texel estimates without mistaking a missing hit lookup for black radiance.</summary>
internal static class SurfaceFallbackEstimates
{
    /// <summary>Includes confirmed sky in the full denominator and rejects any unresolved or nonfinite contribution.</summary>
    public static ImmutableArray<SurfaceFallbackCommit> Resolve(SurfaceFallbackResult result, ReadOnlySpan<SurfaceLightingQuery> answers)
    {
        if (answers.Length != result.Queries.Length) throw new ArgumentException("Mismatched fallback query batch.");
        var commits = ImmutableArray.CreateBuilder<SurfaceFallbackCommit>();
        foreach (var texel in result.Texels)
        {
            if (!texel.Complete) continue;
            Vector3 sum = Vector3.Zero;
            bool valid = true;
            for (int i=texel.FirstQuery; i<texel.FirstQuery+texel.QueryCount; i++)
            {
                var value = answers[i].Result;
                if (value.W != 1 || !float.IsFinite(value.X) || !float.IsFinite(value.Y) || !float.IsFinite(value.Z)) { valid=false; break; }
                sum += Vector3.Max(Vector3.Zero,new(value.X,value.Y,value.Z));
            }
            Vector3 estimate = sum * (MathF.PI / texel.Request.Fraction.W);
            if (!valid || !float.IsFinite(estimate.X) || !float.IsFinite(estimate.Y) || !float.IsFinite(estimate.Z)) continue;
            commits.Add(new() { Page=texel.Request.Page,Slot=texel.Request.Slot,Patch=texel.Request.Patch,Linear=texel.Request.Linear,Estimate=new(estimate,1) });
        }
        return commits.ToImmutable();
    }
}
