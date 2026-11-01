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
        using var decoded=(projections??=new()).Apply(image,"return roundEven(a*255);");
        return Compare(decoded,expected.Select(value=>(float)value).ToArray());
    }

    /// <summary>Retains scalar float addition order for legacy numerical oracles instead of silently substituting a double reduction.</summary>
    internal float OrderedFloatSum(GpuTexture image,string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);
        // Only one invocation accumulates; every other output is zero. The ordinary reduction
        // independently proves the full output domain and returns the one rounded float sum.
        string body="if(index!=0u)return vec4(0);precise float sum=0;"+
            "for(int z=0;z<extent.z;z++)for(int y=0;y<extent.y;y++)for(int x=0;x<extent.x;x++){"+
            "vec4 value=vec4(texelFetch(sourceImage,ivec3(x,y,z),0));sum=sum+("+term+");}return vec4(sum,0,0,0);";
        using var projected=(projections??=new()).Apply(image,body);
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
        return Run(actual,21,input,null,0,double.NegativeInfinity,double.PositiveInfinity,channelMask,predicate:"""
            vec4 expected=uintBitsToFloat(texelFetch(expectedImage,ivec3(coordinate.xy,coordinate.z*2),0));
            vec4 allowed=uintBitsToFloat(texelFetch(expectedImage,ivec3(coordinate.xy,coordinate.z*2+1),0));
            precise vec4 difference=abs(a-expected);
            bool valid=true;
            for(int channel=0;channel<4;channel++)if((channels&(1u<<channel))!=0u)
                valid=valid && difference[channel]<=allowed[channel];
            return bvec2(valid,false);
            """);
    }

    #endregion

    #region Authored domains
    /// <summary>Checks every independently nominated texel, including explicit array layers, without downloading the enclosing image.</summary>
    internal GpuImageResult PredicateAt(GpuTexture image,IReadOnlyList<(int X,int Y,int Layer)> positions,string body)
    {
        RequireContext();ArgumentNullException.ThrowIfNull(positions);ArgumentException.ThrowIfNullOrWhiteSpace(body);
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
        return Predicate(domain,"ivec3 selected=ivec3(a.xyz);vec4 value=texelFetch(historyImage,selected,0);"+body,
            image,mappedSources:true);
    }

    /// <summary>
    /// Evaluates every original texel using GLSL variables a, b, c, d, coordinate, index and multipliers.
    /// The body returns bvec2(contractSatisfied, witnessSelected); witnesses are counted independently
    /// of failures so conditional assertions cannot pass without exercising their selected branch.
    /// Explicit mappedSources permits different source dimensions: b/c/d clamp to their final texel,
    /// while the authored body can select independent coordinates through the named sampler uniforms.
    /// </summary>
    internal GpuImageResult Predicate(GpuTexture image, string body, GpuTexture? second = null,
        GpuTexture? third = null, GpuTexture? fourth = null, Vector4 parameters = default,bool mappedSources=false)
    {
        RequireContext();
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
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
            1, multipliers: parameters, additional: additional?.Texture, fourth: last?.Texture, predicate: body);
    }
    #endregion
    #endregion
}
