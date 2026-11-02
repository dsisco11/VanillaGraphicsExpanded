using System.Numerics;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Retains independent trace observations for the lifetime of one isolated component test.</summary>
public abstract partial class NearFieldShaderTestBase
{
    private readonly List<GpuImageSet> traceCaptures = [];

    #region Protected API
    /// <summary>Releases every retained observation before its original fixture context and program owners retire.</summary>
    protected override void Dispose(bool disposing)
    {
        try
        {
            if (disposing)
            {
                List<Exception>? failures = null;
                foreach (var capture in traceCaptures)
                    try { capture.Dispose(); } catch (Exception error) { (failures ??= []).Add(error); }
                traceCaptures.Clear();
                if (failures is not null) throw new AggregateException("Trace observation cleanup failed.", failures);
            }
        }
        finally { base.Dispose(disposing); }
    }

    /// <summary>Exposes independently retained encoded radiance and packed metadata, without transferring resource ownership.</summary>
    private protected sealed record TraceImages(GpuTexture Radiance, GpuTexture Meta, float RgbDecode);

    /// <summary>Copies both complete original domains; the fixture owns cleanup even when a subsequent assertion throws.</summary>
    private protected TraceImages CaptureTrace(GpuTexture radiance, GpuTexture metadata, float? rgbDecode = null)
    {
        var capture = new GpuImageSet([radiance, metadata]);
        traceCaptures.Add(capture);
        return new(capture[0].Texture, capture[1].Texture, rgbDecode ?? 1 / LightingStorage.PersistentScale);
    }

    /// <summary>Checks correlated physical radiance and metadata, retaining the original exact power-of-two storage decode.</summary>
    private protected GpuPredicateResult ObserveTrace(TraceImages result, GpuAssertionPredicate predicate, TraceImages? other = null,
        Vector4 parameters = default)
    {
        // Only radiance RGB is decoded. Distances and metadata retain their original represented bits.
        return ImageAssertions.Predicates.Predicate(result.Radiance, predicate, result.Meta, other?.Radiance ?? result.Radiance,
            other?.Meta ?? result.Meta, parameters, evaluation:new(result.RgbDecode,(other??result).RgbDecode,1,2));
    }

    /// <summary>Preserves a deliberately local first-texel physical radiance probe with one bounded read.</summary>
    private protected static float TraceRed(TraceImages result) =>
        GpuLocalRead.Scalar(result.Radiance, 0) * result.RgbDecode;

    /// <summary>Correlates screen-control metadata with actual radiance/metadata and an optional offscreen radiance control.</summary>
    private protected GpuPredicateResult ObserveScreenTrace(TraceImages result, TraceImages screenControl, GpuAssertionPredicate predicate,
        TraceImages? offscreen = null, Vector4 parameters = default)
    {
        return ImageAssertions.Predicates.Predicate(result.Radiance, predicate, result.Meta, screenControl.Meta,
            offscreen?.Radiance ?? result.Radiance, parameters, evaluation:new(result.RgbDecode,1,(offscreen??result).RgbDecode,3));
    }
    #endregion
}
