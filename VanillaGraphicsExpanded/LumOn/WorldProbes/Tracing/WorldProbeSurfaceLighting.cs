using System;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Combines GPU lighting answers with unchanged CPU geometry, distance and occlusion metadata.</summary>
internal static class WorldProbeSurfaceLighting
{
    #region Completion
    /// <summary>Requires every hit in a probe batch to resolve; valid black adds a sample while unavailable data retries.</summary>
    public static LumOnWorldProbeTraceResult Resolve(in LumOnWorldProbeTraceResult source,
        ReadOnlySpan<SurfaceLightingQuery> answers, ref int index)
    {
        var samples=(LumOnWorldProbeAtlasSample[])source.AtlasSamples.Clone();
        bool valid=source.Success;
        for (int i=0;i<samples.Length;i++)
        {
            if (!samples[i].SurfaceHit.HasValue) continue;
            if (index>=answers.Length) { valid=false; continue; }
            Vector4 value=answers[index++].Result;
            bool ready=value.W==1 && float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
            valid &= ready;
            samples[i]=samples[i] with { RadianceRgb=ready ? Vector3.Max(Vector3.Zero,new(value.X,value.Y,value.Z)) : Vector3.Zero, SurfaceHit=null };
        }
        return source with { Success=valid, AtlasSamples=samples };
    }
    #endregion
}
