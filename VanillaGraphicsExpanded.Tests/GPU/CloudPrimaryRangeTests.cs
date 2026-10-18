using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Protects the original finite primary lattice while omitting certified empty prefixes and suffixes.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudPrimaryRangeTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Exercises inclusive, thin, distant and clipped support using one shared compute program.</summary>
    [Fact]
    public void ConservativeWindowPreservesEverySupportedScheduledPoint()
    {
        EnsureShaderTestAvailable();
        using var probe=new CloudTraversalProbe(Programs.Api);
        using var map=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgba32f);
        using var volume=new CloudVolumeParameters();volume.CaptureMap(1,default,1,CloudVolumeSettings.Default);
        var cases=new (Vector2 Span,Vector2 Support,int Count,float Jitter)[]
        {
            (new(0,10000),new(4000,4200),384,.173f),
            (new(0,10000),new(0,10000),384,.5f),
            (new(10,110),new(35,35),4,0),
            (new(10,110),new(85,85),4,0),
            (new(10,110),new(35,35),4,MathF.BitDecrement(1)),
            (new(1,2),new(1,2),2,.5f),
            (new(1000000,1000100),new(1000030,1000031),96,.173f),
            (new(0,100),new(-20,20),96,.5f),
            (new(0,100),new(80,120),96,.5f),
            (new(0,100),new(200,210),96,.5f)
        };
        foreach(var test in cases)
        {
            Vector4[] result=probe.Run(volume,map,new(test.Span,test.Count),new(test.Support,test.Jitter),0,12);
            int first=(int)result[402].X,end=(int)result[402].Y;
            Assert.InRange(first,0,test.Count);Assert.InRange(end,first,test.Count);
            Assert.Equal(result[402].Z,result[402].W);
            for(int i=0;i<test.Count;i++)
            {
                double expected=test.Span.X+(i+(double)test.Jitter)*(test.Span.Y-test.Span.X)/test.Count;
                Assert.InRange(Math.Abs(result[i].X-expected),0,Math.Max(1e-6,Math.Abs(expected)*3e-7));
                Assert.Equal(result[i].Y,result[i].Z);
                if(result[i].Y>0)Assert.True(i>=first&&i<end,"A certified window omitted a supported lattice point.");
            }
            if(test.Count==384&&test.Support.Y-test.Support.X<1000)
                Assert.True(end-first<test.Count/2,"Thin support must avoid the unused majority of scheduled queries.");
        }
    }
    #endregion
}
