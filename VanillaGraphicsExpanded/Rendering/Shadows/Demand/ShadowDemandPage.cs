using System;
using VanillaGraphicsExpanded.Rendering.Shadows.Pages;
using VanillaGraphicsExpanded.Rendering.Shadows.Projections;
using VanillaGraphicsExpanded.Rendering.Shadows.Sampling;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Demand;

/// <summary>One depth-inclusive enclosure in its selected rendered mapping, separate from current illumination.</summary>
internal sealed record ShadowDemandPage(ShadowPageRequest Request, ShadowProjectionSnapshot Current, int Detail = 0)
{
    #region Public API
    /// <summary>Produces GPU containment/temporal inputs without inventing a sampling point inside the region.</summary>
    internal uint[] Pack(double now, Configuration.ShadowTemporalSettings temporal, int capacity, int probes, int layers)
    {
        var words = new uint[160]; Request.Pack().CopyTo(words, 0);
        if (Request.Projection is not { } rendered || rendered.Source != Current.Source || rendered.IsPoint != Current.IsPoint)
            throw new InvalidOperationException("Demand requires compatible rendered and current source projections.");
        new ShadowTemporalQuery(Current, now, temporal).WriteTo(words);
        words[116] = (uint)capacity; words[117] = (uint)probes; words[119] = (uint)layers;
        words[147]=checked((uint)Detail);
        return words;
    }
    #endregion
}
