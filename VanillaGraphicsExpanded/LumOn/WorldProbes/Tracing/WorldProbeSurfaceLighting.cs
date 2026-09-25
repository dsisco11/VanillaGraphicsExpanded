using System;
using System.Collections.Immutable;
using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;

/// <summary>Combines GPU lighting answers with unchanged CPU geometry, distance and occlusion metadata.</summary>
internal static class WorldProbeSurfaceLighting
{
    #region Completion
    /// <summary>Separates ready directions from unresolved hits; missing answers never become valid black samples.</summary>
    public static LumOnWorldProbeTraceResult Resolve(in LumOnWorldProbeTraceResult source,
        ReadOnlySpan<SurfaceLightingQuery> answers, ref int index)
    {
        var readySamples = ImmutableArray.CreateBuilder<LumOnWorldProbeAtlasSample>(source.AtlasSamples.IsDefault ? 0 : source.AtlasSamples.Length);
        var retrySamples = ImmutableArray.CreateBuilder<LumOnWorldProbeAtlasSample>();
        foreach (var sample in source.AtlasSamples.AsSpan())
        {
            if (!sample.SurfaceHit.HasValue) { readySamples.Add(sample); continue; }
            if (index>=answers.Length) { retrySamples.Add(sample); continue; }
            Vector4 value=answers[index++].Result;
            bool ready=value.W==1 && float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
            if (ready)
                readySamples.Add(sample with { RadianceRgb=Vector3.Max(Vector3.Zero,new(value.X,value.Y,value.Z)), SurfaceHit=null });
            else retrySamples.Add(sample);
        }
        return source with { Success=source.Success && readySamples.Count>0,
            AtlasSamples=readySamples.ToImmutable(), RetrySamples=retrySamples.ToImmutable() };
    }
    #endregion
}
