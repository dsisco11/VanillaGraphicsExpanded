using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Assertions;

/// <summary>Owns selection observation policy with explicit fixture dependencies.</summary>
internal sealed class GpuImageSelection : GpuObservationService
{
    private readonly GpuImageStatistics statistics;
    private readonly GpuImagePredicates predicates;
    private readonly GpuImageTransform projections;

    #region Internal API
    /// <summary>Borrows prepared executables and related observation services from the fixture owner.</summary>
    internal GpuImageSelection(GpuObservationPrograms programs, GpuImageStatistics statistics, GpuImagePredicates predicates, GpuImageTransform projections) : base(programs)
    {
        this.statistics = statistics; this.predicates = predicates;
        this.projections = projections;
    }

    /// <summary>Finds the first finite floating extremum in an explicit domain and returns coordinates relative to that domain.</summary>
    internal (int X,int Y,int Layer,float Value) Extremum(GpuTexture image,int channel,bool maximum,RHITextureRegion? selection=null)
    {
        Programs.RequireContext();using var selected=new GpuImageSnapshot(image,selection);
        if(selected.Kind!=0 || channel<0 || channel>=selected.Channels)throw new ArgumentException("Extremum selection requires a represented floating channel.");
        var bounds=statistics.Inspect(selected.Texture,channelMask:1u<<channel);bounds.AssertFiniteRange();
        float value=(float)(maximum?bounds.Maximum:bounds.Minimum);
        var witness=predicates.Predicate(selected.Texture, ObservationPredicates.GpuImageAssertionsSelection_Extremum,
            parameters:new(value,channel,0,0));
        int index=witness.FirstWitnessIndex,width=selected.Texture.Width,height=selected.Texture.Height;
        return(index%width,index/width%height,index/(width*height),value);
    }

    /// <summary>Finds the first texel of maximum authored RGB luminance, then reads only that local RGB tuple.</summary>
    internal (Vector3 Color,float Luminance) Brightest(GpuTexture image)
    {
        Programs.RequireContext();
        using var luminance=projections.Apply(image, ObservationProjections.GpuImageAssertionsSelection_Brightest);
        var observation=statistics.Inspect(luminance,channelMask:1);observation.AssertFiniteRange();
        float maximum=(float)observation.Maximum;
        var witness=predicates.Predicate(luminance, ObservationPredicates.GpuImageAssertionsSelection_Brightest_2,parameters:new(maximum,0,0,0));
        int index=witness.FirstWitnessIndex,x=index%image.Width,y=index/image.Width%image.Height,layer=index/(image.Width*image.Height);
        int slice=image.TextureTarget==TextureDimension.Three?0:layer,z=image.TextureTarget==TextureDimension.Three?layer:0;
        using var selected=new GpuImageSnapshot(image,new(new(0,slice),x,y,z,1,1));
        float[] pixel=selected.Texture.ReadPixelsRegion(0,0,1,1);
        Assert.True(pixel.Length>=3,"Luminance selection requires the original RGB tuple.");
        return (new(pixel[0],pixel[1],pixel[2]),maximum);
    }

    #endregion
}
