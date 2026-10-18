using Newtonsoft.Json;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks independently rounded trace/history dimensions, storage bounds and immutable phase controls.</summary>
public sealed class CloudResolutionTests
{
    #region Public API
    /// <summary>Odd and tiny screens retain half reconstruction and independently rounded physical trace extents.</summary>
    [Theory]
    [InlineData(1,1)][InlineData(2,3)][InlineData(5,3)][InlineData(9,7)][InlineData(1920,1080)]
    public void LayoutSeparatesTraceAndReconstruction(int width,int height)
    {
        foreach(var scale in new[]{CloudTraceScale.Half,CloudTraceScale.Quarter})
        {
            var layout=CloudResolutionLayout.Create(width,height,scale);
            Assert.Equal((width+1)/2,layout.Width);Assert.Equal((height+1)/2,layout.Height);
            Assert.Equal((int)Math.Ceiling(width/(double)(int)scale),layout.TraceWidth);
            Assert.Equal((int)Math.Ceiling(height/(double)(int)scale),layout.TraceHeight);
            long bytes=scale==CloudTraceScale.Half?144L*layout.Width*layout.Height:
                48L*layout.TraceWidth*layout.TraceHeight+96L*layout.Width*layout.Height;
            Assert.Equal(bytes,layout.NominalBytes);
        }
    }

    /// <summary>Every phase belongs to the half-grid four-frame cycle and the physical trace extent stays separate.</summary>
    [Fact]
    public void ParametersPackFourReconstructionPhases()
    {
        using var parameters=new CloudTemporalParameters();
        float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        for(uint frame=0;frame<8;frame++)
        {
            parameters.Capture(identity,default,5,4,frame,unchecked(frame-1),true,CloudTraceScale.Quarter);
            var packed=MemoryMarshal.Cast<byte,float>(parameters.Bytes);
            Assert.Equal(128,parameters.SizeBytes);Assert.Equal(5,packed[20]);Assert.Equal(4,packed[21]);
            Assert.Equal(frame%2,packed[22]);Assert.Equal(frame/2%2,packed[23]);
            Assert.Equal(0,packed[25]);Assert.Equal(0,packed[26]);
            Assert.Equal(3,packed[28]);Assert.Equal(2,packed[29]);Assert.Equal(2,packed[30]);
        }
    }

    /// <summary>Persisted Half remains the default; explicit Quarter and reference selection retain independent meanings.</summary>
    [Theory]
    [InlineData("{}",CloudTraceScale.Half)][InlineData("{\"TraceScale\":4}",CloudTraceScale.Quarter)]
    [InlineData("{\"TraceScale\":\"Quarter\"}",CloudTraceScale.Quarter)]
    [InlineData("{\"TraceScale\":3}",CloudTraceScale.Half)]
    public void ControlsUseCleanDefaultsAndRoundTrip(string json,CloudTraceScale expected)
    {
        var value=JsonConvert.DeserializeObject<CloudRenderingSettings>(json)!;
        Assert.Equal(expected,value.Capture().TraceScale);
        value.FullResolutionReference=true;
        var copy=JsonConvert.DeserializeObject<CloudRenderingSettings>(JsonConvert.SerializeObject(value))!;
        Assert.Equal(expected,copy.Capture().TraceScale);Assert.True(copy.FullResolutionReference);
        JsonConvert.PopulateObject("{\"ViewRaySampleMax\":96}",copy);
        Assert.Equal(expected,copy.Capture().TraceScale);
    }

    /// <summary>Unknown enums and excessive combined allocations reject before creating resources.</summary>
    [Fact]
    public void InvalidLayoutsCannotAllocate()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>CloudResolutionLayout.Create(16384,16384,CloudTraceScale.Quarter));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CloudResolutionLayout.Create(0,1,CloudTraceScale.Half));
        Assert.Throws<ArgumentOutOfRangeException>(()=>(CloudVolumeSettings.Default with {TraceScale=(CloudTraceScale)3}).Validate());
        var quarter=CloudResolutionLayout.Create(6000,6000,CloudTraceScale.Quarter);
        Assert.True(quarter.NominalBytes<=1024L*1024*1024);
        Assert.Throws<ArgumentOutOfRangeException>(()=>CloudResolutionLayout.Create(6000,6000,CloudTraceScale.Half));
    }
    #endregion
}
