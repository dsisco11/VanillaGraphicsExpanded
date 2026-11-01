using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Selects local observations with complete-domain GPU reductions and deterministic original texel ordering.</summary>
internal sealed partial class GpuImageAssertions
{
    #region Internal API
    /// <summary>Finds the first finite floating extremum in an explicit domain and returns coordinates relative to that domain.</summary>
    internal (int X,int Y,int Layer,float Value) Extremum(GpuTexture image,int channel,bool maximum,RHITextureRegion? selection=null)
    {
        RequireContext();using var selected=new GpuImageSnapshot(image,selection);
        if(selected.Kind!=0 || channel<0 || channel>=selected.Channels)throw new ArgumentException("Extremum selection requires a represented floating channel.");
        var bounds=Inspect(selected.Texture,channelMask:1u<<channel);bounds.AssertFiniteRange();
        float value=(float)(maximum?bounds.Maximum:bounds.Minimum);
        var witness=Predicate(selected.Texture,"return bvec2(true,equalBits(floatBitsToUint(a[int(multipliers.y)]),floatBitsToUint(multipliers.x)));",
            parameters:new(value,channel,0,0));
        int index=witness.FirstWitnessIndex,width=selected.Texture.Width,height=selected.Texture.Height;
        return(index%width,index/width%height,index/(width*height),value);
    }

    /// <summary>Finds the first texel of maximum authored RGB luminance, then reads only that local RGB tuple.</summary>
    internal (Vector3 Color,float Luminance) Brightest(GpuTexture image)
    {
        RequireContext();
        using var luminance=(projections??=new()).Apply(image,"return vec4(dot(a.rgb,vec3(0.2126,0.7152,0.0722)),0,0,0);");
        var observation=Inspect(luminance,channelMask:1);observation.AssertFiniteRange();
        float maximum=(float)observation.Maximum;
        var witness=Predicate(luminance,"return bvec2(true,a.r==multipliers.x);",parameters:new(maximum,0,0,0));
        int index=witness.FirstWitnessIndex,x=index%image.Width,y=index/image.Width%image.Height,layer=index/(image.Width*image.Height);
        int slice=image.TextureTarget==TextureDimension.Three?0:layer,z=image.TextureTarget==TextureDimension.Three?layer:0;
        using var selected=new GpuImageSnapshot(image,new(new(0,slice),x,y,z,1,1));
        float[] pixel=selected.Texture.ReadPixelsRegion(0,0,1,1);
        Assert.True(pixel.Length>=3,"Luminance selection requires the original RGB tuple.");
        return (new(pixel[0],pixel[1],pixel[2]),maximum);
    }
    #endregion
}
