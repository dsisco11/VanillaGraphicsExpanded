using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Executes independently authored component predicates over corresponding retained GPU images.</summary>
internal sealed partial class GpuImageAssertions
{
    #region Internal API
    #region Numerical references
    /// <summary>Compares every RGBA8 metadata byte with independently authored input after exact normalized-byte reconstruction.</summary>
    internal GpuImageResult CompareRgba8(GpuTexture image,byte[] expected)
    {
        RequireContext();
        if(image.InternalFormat!=TextureFormat.Rgba8)throw new ArgumentException("Byte metadata comparison requires RGBA8 storage.",nameof(image));
        using var decoded=(projections??=new()).Apply(image, GpuImageProjection.GpuImageAssertionsPredicates_CompareRgba8);
        return Compare(decoded,expected.Select(value=>(float)value).ToArray());
    }

    /// <summary>Retains scalar float addition order for legacy numerical oracles instead of silently substituting a double reduction.</summary>
    internal float OrderedFloatSum(GpuTexture image,GpuImageProjection term)
    {
        using var projected=(projections??=new()).Apply(image,term);
        var result=Statistics(projected,channelMask:1);
        result.AssertFiniteRange();return (float)result.Sum(0);
    }
    /// <summary>Integrates every selected image component against independent double-precision quadrature weights.</summary>
    internal GpuImageResult WeightedStatistics(GpuTexture image,double[] weights,uint? channelMask=null,double[]? secondaryWeights=null)
    {
        RequireContext();using var actual=new GpuImageSnapshot(image);
        int count=checked(actual.Texture.Width*actual.Texture.Height*actual.Texture.Depth);
        if(actual.Kind!=0 || weights.Length!=count || (secondaryWeights is not null && secondaryWeights.Length!=count))
            throw new ArgumentException("Weighted moments require floating images and one weight per original texel.");
        uint[] bits=new uint[checked(count*4)];
        for(int pixel=0;pixel<count;pixel++)
        {
            if(!double.IsFinite(weights[pixel]))throw new ArgumentException("Quadrature weights must be finite.",nameof(weights));
            ulong value=BitConverter.DoubleToUInt64Bits(weights[pixel]);
            bits[pixel*4]=(uint)value;bits[pixel*4+1]=(uint)(value>>32);
            double secondary=secondaryWeights?[pixel]??1;
            if(!double.IsFinite(secondary))throw new ArgumentException("Secondary quadrature weights must be finite.",nameof(secondaryWeights));
            value=BitConverter.DoubleToUInt64Bits(secondary);
            bits[pixel*4+2]=(uint)value;bits[pixel*4+3]=(uint)(value>>32);
        }
        using var input=CreateArray(TextureFormat.Rgba32ui,actual.Texture.Width,actual.Texture.Height,actual.Texture.Depth);
        input.UploadDataImmediate(bits,0,0,0,input.Width,input.Height,input.Depth);
        return Run(actual,11,input,null,0,double.NegativeInfinity,double.PositiveInfinity,channelMask,
            statisticExpression:3);
    }

    /// <summary>Checks independently authored expectations using float subtraction and each component's original absolute tolerance.</summary>
    internal GpuImageResult CompareFloating(GpuTexture image,float[] expected,float[] tolerances,uint? channelMask=null)
    {
        RequireContext();
        using var actual=new GpuImageSnapshot(image);
        int plane=checked(actual.Texture.Width*actual.Texture.Height),count=checked(plane*actual.Texture.Depth);
        if(actual.Kind!=0 || expected.Length!=checked(count*actual.Channels) || tolerances.Length!=expected.Length)
            throw new ArgumentException("Float expectations and tolerances must cover every original component.");
        uint[] bits=new uint[checked(count*8)];
        for(int pixel=0;pixel<count;pixel++)for(int channel=0;channel<actual.Channels;channel++)
        {
            int scalar=pixel*actual.Channels+channel;
            if(!float.IsFinite(expected[scalar]) || !float.IsFinite(tolerances[scalar]) || tolerances[scalar]<0)
                throw new ArgumentException("Float expectations require finite values and nonnegative finite tolerances.");
            int address=((pixel/plane*2)*plane+pixel%plane)*4+channel;
            bits[address]=BitConverter.SingleToUInt32Bits(expected[scalar]);
            bits[address+plane*4]=BitConverter.SingleToUInt32Bits(tolerances[scalar]);
        }
        using var input=CreateArray(TextureFormat.Rgba32ui,actual.Texture.Width,actual.Texture.Height,actual.Texture.Depth*2);
        input.UploadDataImmediate(bits,0,0,0,input.Width,input.Height,input.Depth);
        return Run(actual,21,input,null,0,double.NegativeInfinity,double.PositiveInfinity,channelMask, predicate: GpuAssertionPredicate.GpuImageAssertionsPredicates_CompareFloating);
    }

    #endregion

    #region Authored domains
    /// <summary>Checks every independently nominated texel, including explicit array layers, without downloading the enclosing image.</summary>
    internal GpuImageResult PredicateAt(GpuTexture image,IReadOnlyList<(int X,int Y,int Layer)> positions,GpuAssertionPredicate body)
    {
        RequireContext();ArgumentNullException.ThrowIfNull(positions);
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
    internal GpuImageResult Predicate(GpuTexture image, GpuAssertionPredicate body, GpuTexture? second = null,
        GpuTexture? third = null, GpuTexture? fourth = null, Vector4 parameters = default,bool mappedSources=false, Vector4? evaluation=null)
    {
        RequireContext();
        if (body == GpuAssertionPredicate.None || !Enum.IsDefined(body))
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
        return Run(actual, 21, null, reference?.Texture, 0, double.NegativeInfinity, double.PositiveInfinity,
            1, multipliers: parameters, additional: additional?.Texture, fourth: last?.Texture, predicate: body, evaluation: evaluation);
    }
    #endregion
    #endregion
}
