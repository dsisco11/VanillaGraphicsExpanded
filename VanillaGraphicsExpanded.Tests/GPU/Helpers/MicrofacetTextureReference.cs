using System.Numerics;
using VanillaGraphicsExpanded.PBR.Materials.Energy;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>Independently addresses half-quantized texture nodes and their bilinear interpolation.</summary>
internal static class MicrofacetTextureReference
{
    #region Public API
    /// <summary>Binds both the owned texture and its required filtering state for raw fixture programs.</summary>
    internal static void Bind()
    {
        int unit=GpuBindingRegistry.Sampler.MicrofacetEnergy;
        VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindSampledTextures([new(unit, MicrofacetEnergyLookup.Prepare(), GpuSamplers.LinearClamp)]);
    }

    /// <summary>Returns the software-filtered quantized lookup and a local fractional-filter precision envelope.</summary>
    internal static (Vector2 Value,Vector2 Filtered,Vector2 Error) Sample(int size,float roughness,float viewCosine)
    {
        float x=MathF.Sqrt((Math.Clamp(roughness,.04f,1)-.04f)/.96f)*(size-1);
        float y=MathF.Sqrt(MathF.Sqrt(Math.Clamp(viewCosine,0,1)))*(size-1);
        int ix=Math.Min((int)x,size-2),iy=Math.Min((int)y,size-2);
        var a=Node(size,ix,iy); var b=Node(size,ix+1,iy);
        var c=Node(size,ix,iy+1); var d=Node(size,ix+1,iy+1);
        var value=Vector2.Lerp(Vector2.Lerp(a,b,x-ix),Vector2.Lerp(c,d,x-ix),y-iy);
        // A one-part-in-256 displacement in either interpolation weight bounds an
        // eight-fractional-bit filter. The explicit local slopes prevent a fixed
        // tolerance from hiding large coefficient errors or erroneous endpoints.
        var dx=Vector2.Max(Vector2.Abs(b-a),Vector2.Abs(d-c));
        var dy=Vector2.Max(Vector2.Abs(c-a),Vector2.Abs(d-b));
        var filtered=new Vector2((float)(Half)value.X,(float)(Half)value.Y);
        // RG16F filtering may round its result to the source precision. Bound that
        // operation per component using its actual exponent, independently of node rounding.
        var rounding=new Vector2(HalfRoundingBound(value.X),HalfRoundingBound(value.Y));
        return (value,filtered,(dx+dy)/256+rounding+new Vector2(.000002f));
    }
    #endregion

    #region Private
    /// <summary>Bounds nearest binary16 rounding at a finite nonnegative interpolated coefficient.</summary>
    private static float HalfRoundingBound(float value)
    {
        Half rounded=(Half)value;
        return Math.Max((float)Half.BitIncrement(rounded)-(float)rounded,
            (float)rounded-(float)Half.BitDecrement(rounded))*.5f;
    }

    /// <summary>Samples the authoritative full-precision asset at the declared warped-grid node, then rounds to binary16.</summary>
    private static Vector2 Node(int size,int x,int y)
    {
        if(size==128)
        {
            int index=(y*128+x)*2;
            var samples=MicrofacetEnergyTable.Shared.Samples;
            return new((float)(Half)samples[index],(float)(Half)samples[index+1]);
        }
        float u=(float)x/(size-1),v=(float)y/(size-1);
        var value=MicrofacetEnergyTable.Shared.Sample(.04f+.96f*u*u,v*v*v*v);
        return new((float)(Half)value.X,(float)(Half)value.Y);
    }
    #endregion
}
