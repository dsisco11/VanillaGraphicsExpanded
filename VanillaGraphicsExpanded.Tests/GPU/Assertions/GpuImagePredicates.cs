using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns predicates observation policy with explicit fixture dependencies.</summary>
internal sealed class GpuImagePredicates : GpuObservationService
{

    #region Internal API
    /// <summary>Borrows prepared executables and related observation services from the fixture owner.</summary>
    internal GpuImagePredicates(GpuObservationPrograms programs) : base(programs)
    {
    }

    /// <summary>Checks every independently nominated texel, including explicit array layers, without downloading the enclosing image.</summary>
    internal GpuPredicateResult PredicateAt(GpuTexture image,IReadOnlyList<(int X,int Y,int Layer)> positions,GpuAssertionPredicate body)
    {
        Programs.RequireContext();ArgumentNullException.ThrowIfNull(positions);
        if(positions.Count==0)throw new ArgumentException("A sparse assertion requires an explicit nonempty domain.",nameof(positions));
        var coordinates=new float[checked(positions.Count*4)];
        for(int index=0;index<positions.Count;index++)
        {
            var p=positions[index];
            if((uint)p.X>=(uint)image.Width || (uint)p.Y>=(uint)image.Height || (uint)p.Layer>=(uint)image.Depth)
                throw new ArgumentOutOfRangeException(nameof(positions));
            coordinates[index*4]=p.X;coordinates[index*4+1]=p.Y;coordinates[index*4+2]=p.Layer;
        }
        // The authored coordinate image is the dispatch domain. The complete original image
        // remains the sampled source; no source extent, selected layer or nominated point is dropped.
        using var domain=CreateArray(TextureFormat.Rgba32f,positions.Count,1,1);
        domain.UploadDataImmediate(coordinates,0,0,0,positions.Count,1,1);
        return Predicate(domain,body,
            image,mappedSources:true,evaluation:new(1,1,1,1));
    }

    /// <summary>
    /// Evaluates every original texel using GLSL variables a, b, c, d, coordinate, index and multipliers.
    /// The body returns bvec2(contractSatisfied, witnessSelected); witnesses are counted independently
    /// of failures so conditional assertions cannot pass without exercising their selected branch.
    /// Explicit mappedSources permits different source dimensions: b/c/d clamp to their final texel,
    /// while the authored body can select independent coordinates through the named sampler uniforms.
    /// </summary>
    internal GpuPredicateResult Predicate(GpuTexture image, GpuAssertionPredicate body, GpuTexture? second = null,
        GpuTexture? third = null, GpuTexture? fourth = null, Vector4 parameters = default,bool mappedSources=false, Vector4? evaluation=null)
    {
        Programs.RequireContext();
        if (!body.IsDeclared)
            throw new ArgumentOutOfRangeException(nameof(body), "Select a declared independently authored predicate.");
        using var actual = new GpuImageSnapshot(image);
        using var reference = second is null ? null : new GpuImageSnapshot(second);
        using var additional = third is null ? null : new GpuImageSnapshot(third);
        using var last = fourth is null ? null : new GpuImageSnapshot(fourth);
        foreach (var candidate in new[] { actual, reference, additional, last })
        {
            // Different channel counts are intentional for radiance/metadata pairs; dimensions must agree.
            if (candidate is null) continue;
            if (candidate.Kind != 0 || (!mappedSources && (candidate.Texture.Width != actual.Texture.Width ||
                candidate.Texture.Height != actual.Texture.Height || candidate.Texture.Depth != actual.Texture.Depth)))
                throw new ArgumentException("Authored predicates require corresponding floating image domains.");
        }
        var inputs = DomainInputs(actual, 1, 0, double.NegativeInfinity, double.PositiveInfinity);
        GpuAssertionInputs.Integer(inputs, 12, (uint)PredicateOperation.Authored);
        GpuAssertionInputs.Vector(inputs, 64, parameters);
        GpuAssertionInputs.Integer(inputs, 92, (uint)body.Choice);
        GpuAssertionInputs.Integer(inputs, 20, body.Inputs | (evaluation?.W == 1 ? 2u : 0u));
        GpuAssertionInputs.Vector(inputs, 128, evaluation ?? new(1, 1, 1, 0));
        return new(Observe(actual, "predicate-" + body.Domain, inputs, [("actualImage", actual.Texture), ("historyImage", (body.Inputs & 2) != 0 || evaluation?.W == 1 ? reference?.Texture ?? actual.Texture : null), ("additionalImage", (body.Inputs & 8) != 0 ? additional?.Texture ?? actual.Texture : null), ("fourthImage", (body.Inputs & 16) != 0 ? last?.Texture ?? actual.Texture : null)]));
    }

    #endregion

    #region Private
    /// <summary>Identifies only the wire operations implemented by this focused observation service.</summary>
    private enum PredicateOperation
    {
        /// <summary>Selects the Authored kernel operation.</summary>
        Authored = 21,
    }
    #endregion
}
