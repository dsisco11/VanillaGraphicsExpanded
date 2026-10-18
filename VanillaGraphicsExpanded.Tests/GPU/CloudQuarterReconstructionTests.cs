using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks quarter trace scheduling, half-grid reconstruction and independently authored reconstruction contracts.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudQuarterReconstructionTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Sampling-sized support and optical variation accumulate while true support and foreground changes still reject.</summary>
    [Fact]
    public void SamplingUncertaintyAccumulatesWithoutBlendingCurrentSpacingMetadata()
    {
        EnsureShaderTestAvailable();
        foreach(var scale in new[]{CloudTraceScale.Half,CloudTraceScale.Quarter})
        {
            using var view=CreateScene(12,12,scale);
            view.Capture(0,0,10,1,0,"static");
            var layout=CloudResolutionLayout.Create(12,12,scale);
            int sourceCenter=scale==CloudTraceScale.Half?14:4;
            var current=Payload(layout.TraceWidth*layout.TraceHeight,i=>i==sourceCenter?.4f:i%2==0?.2f:.8f);
            var previous=Payload(layout.Width*layout.Height,_=>.6f);
            // The captured support discrepancy must fit a ray's finite primary quadrature spacing,
            // while the previous optical value differs enough to exercise neighborhood clamping.
            for(int pixel=0;pixel<current[0].Length/4;pixel++)
            {
                current[1][pixel*4+3]=10.34542f;
                current[2][pixel*4]=455.19836f;current[2][pixel*4+1]=517.27087f;
            }
            for(int pixel=0;pixel<previous[0].Length/4;pixel++)
            {
                previous[1][pixel*4+3]=10.35651f;
                previous[2][pixel*4]=455.68661f;previous[2][pixel*4+1]=507.46918f;
            }
            const int destination=14,index=destination*4;
            var baseline=view.ReconstructAuthoredTrace(0,false,current,previous);
            foreach(string scenario in new[]{"sampling","support","opaque","liquid"})
            {
                var history=previous.Select(layer=>(float[])layer.Clone()).ToArray();
                for(int pixel=0;pixel<history[0].Length/4;pixel++)
                {
                    if(scenario=="support"){history[2][pixel*4]+=1000;history[2][pixel*4+1]+=1000;}
                    if(scenario=="opaque")history[2][pixel*4+2]=.9f;
                    if(scenario=="liquid")history[2][pixel*4+3]=.9f;
                }
                var actual=view.ReconstructAuthoredTrace(0,true,current,history);
                float expected=scenario=="sampling"?.55f:.4f;
                Assert.InRange(MathF.Abs(actual[0][index+3]-expected),0,1e-5f);
                for(int channel=0;channel<3;channel++)
                {
                    Assert.InRange(MathF.Abs(actual[0][index+channel]-expected*10),0,1e-4f);
                    Assert.InRange(MathF.Abs(actual[1][index+channel]-expected),0,1e-5f);
                }
                Assert.Equal(baseline[1][index+3],actual[1][index+3]);
                Assert.True(actual[1][index+3]>0);
                if(scale==CloudTraceScale.Half)Assert.Equal(10.34542f,actual[1][index+3]);
                for(int channel=0;channel<4;channel++)Assert.Equal(baseline[2][index+channel],actual[2][index+channel]);
            }
        }
    }
    /// <summary>Actual trace depth identities expose every scheduled full-view pixel through all four phases and odd edges.</summary>
    [Fact]
    public void TraceUsesFourHalfGridPhasesAtOddEdges()
    {
        EnsureShaderTestAvailable();
        const int width=9,height=7;
        using var view=CreateScene(width,height,CloudTraceScale.Quarter);
        var layout=CloudResolutionLayout.Create(width,height,CloudTraceScale.Quarter);
        float[] depths=Enumerable.Range(0,width*height).Select(i=>.9f+i*.0001f).ToArray();
        float[] liquids=Enumerable.Range(0,width*height).Select(i=>.8f+i*.0001f).ToArray();
        for(uint frame=0;frame<4;frame++)
        {
            view.Capture(frame,0,10,1,0,"static");view.SetDepthFields(depths,Enumerable.Repeat(1f,width*height).ToArray(),liquids);
            view.RenderTrace(frame);var raw=view.ReadTraceRecord();
            Assert.Equal(layout.TraceWidth*layout.TraceHeight*4,raw[2].Length);
            for(int y=0;y<layout.TraceHeight;y++)for(int x=0;x<layout.TraceWidth;x++)
            {
                int hx=Math.Min(2*x+(int)(frame%2),layout.Width-1),hy=Math.Min(2*y+(int)(frame/2%2),layout.Height-1);
                int fullX=Math.Min(2*hx,width-1),fullY=Math.Min(2*hy,height-1),index=(y*layout.TraceWidth+x)*4;
                Assert.Equal(depths[fullY*width+fullX],raw[2][index+2]);
                Assert.Equal(liquids[fullY*width+fullX],raw[2][index+3]);
            }
        }
    }

    /// <summary>Shared current weights, unsampled history reuse and sampled temporal blending follow independently authored arithmetic.</summary>
    [Fact]
    public void UnsampledLocationsReuseCompatibleHistory()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(9,7,CloudTraceScale.Quarter);
        view.Capture(0,0,10,1,0,"static");
        float[][] current=Payload(6,i=>.4f+(i%2)*.04f),previous=Payload(20,_=>.41f);
        var invalid=view.ReconstructAuthoredTrace(0,false,current,previous);
        var valid=view.ReconstructAuthoredTrace(0,true,current,previous);
        Assert.InRange(MathF.Abs(valid[0][1*4+3]-.41f),0,1e-5f);
        Assert.InRange(MathF.Abs(valid[0][0*4+3]-(.4f*.25f+.41f*.75f)),0,1e-5f);
        Assert.True(MathF.Abs(invalid[0][1*4+3]-.41f)>1e-4f,"The fixture must distinguish history reuse from current-only interpolation.");
        Assert.InRange(MathF.Abs(valid[1][1*4]-.41f),0,1e-5f);
        view.Capture(0,0,10,1,0,"static");var weighted=Payload(6,i=>.2f+i*.1f);
        var data=view.ReconstructAuthoredTrace(0,false,weighted);
        // Destination (1,0) sees the bounded 2x2 trace neighborhood at half positions (0,0),(2,0),(0,2),(2,2).
        double total=0,sum=0;
        for(int y=0;y<2;y++)for(int x=0;x<2;x++)
        {
            double weight=1.0/(1+(2*x-1)*(2*x-1)+4*y*y);
            total+=weight;sum+=weighted[0][(y*3+x)*4+3]*weight;
        }
        float expected=(float)(sum/total);
        Assert.InRange(MathF.Abs(data[0][7]-expected),0,1e-6f);
        for(int channel=0;channel<3;channel++)
        {
            Assert.InRange(MathF.Abs(data[0][4+channel]-expected*10),0,1e-5f);
            Assert.InRange(MathF.Abs(data[1][4+channel]-expected),0,1e-6f);
        }
    }

    /// <summary>Quarter history reconstructs native OIT products and applies scene storage only at final resolve.</summary>
    [Fact]
    public void ResolveEncodesAuthoredPhysicalProductsOnce()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(9,7,CloudTraceScale.Quarter);
        foreach(float scale in new[]{1f,16f})
        {
            view.Capture(0,0,10,scale,0,"static");var data=view.ResolveAuthoredHistory();
            Assert.All(data[0],value=>Assert.InRange(MathF.Abs(value-.6f),0,1e-5f));
            Assert.All(data[1],value=>Assert.InRange(MathF.Abs(value-.4f),0,1e-5f));
            for(int bucket=0;bucket<3;bucket++)for(int pixel=0;pixel<63;pixel++)
            {
                for(int channel=0;channel<3;channel++)Assert.InRange(MathF.Abs(data[3+bucket][pixel*4+channel]-3.2f*scale),0,1e-4f);
                Assert.InRange(MathF.Abs(data[3+bucket][pixel*4+3]-.24f),0,1e-5f);
            }
        }
    }

    /// <summary>Destination depth rejects both current neighbors and stale history without introducing a hidden transport fallback.</summary>
    [Fact]
    public void RejectedUnsampledSupportWritesNeutralWithCurrentDepth()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(9,7,CloudTraceScale.Quarter);
        view.Capture(0,0,10,1,0,"static");
        foreach(string clip in new[]{"material","visibility","liquid"})
        {
            float[] depths=Enumerable.Repeat(1f,63).ToArray();const int fullPixel=2;
            float foreground=(1.00020002f-.200020002f)*.5f+.5f;depths[fullPixel]=foreground;
            float[] clear=Enumerable.Repeat(1f,63).ToArray();
            view.SetDepthFields(clip=="material"?depths:clear,clip=="visibility"?depths:clear,clip=="liquid"?depths:clear);
            var data=view.ReconstructAuthoredTrace(0,true,Payload(6,_=>.4f),Payload(20,_=>.41f));
            const int destination=1;
            Assert.Equal(1,data[0][destination*4+3]);Assert.Equal(0,data[0][destination*4]);
            for(int channel=0;channel<3;channel++)Assert.Equal(1,data[1][destination*4+channel]);
            Assert.Equal(0,data[2][destination*4]);Assert.Equal(0,data[2][destination*4+1]);
            Assert.Equal(clip=="liquid"?1:foreground,data[2][destination*4+2]);
            Assert.Equal(clip=="liquid"?foreground:1,data[2][destination*4+3]);
        }
    }
    #endregion

    #region Private
    /// <summary>Creates actual trace/temporal/resolve owners with an explicitly selected physical trace layout.</summary>
    private CloudReconstructionScene CreateScene(int width,int height,CloudTraceScale scale)=>new(width,height,
        Programs,false,4,settings:CloudVolumeSettings.Default with {TraceScale=scale});

    /// <summary>Authors independent optical factors and conservative support/depth for a known number of texels.</summary>
    private static float[][] Payload(int pixels,Func<int,float> factor)
    {
        float[][] data=Enumerable.Range(0,3).Select(_=>new float[pixels*4]).ToArray();
        for(int i=0;i<pixels;i++)
        {
            float value=factor(i);
            for(int channel=0;channel<4;channel++)
            {
                data[0][i*4+channel]=channel==3?value:value*10;
                data[1][i*4+channel]=channel==3?0:value;
            }
            data[2][i*4]=10;data[2][i*4+1]=20;data[2][i*4+2]=data[2][i*4+3]=1;
        }
        return data;
    }

    #endregion
}
