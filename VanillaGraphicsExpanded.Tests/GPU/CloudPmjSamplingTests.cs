using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual GLSL opportunity counts and bounded deterministic subpixel sampling.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudPmjSamplingTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Cold, carried, clamped-edge and wrapped schedules consume consecutive points rather than frame-strided subsequences.</summary>
    [Fact]
    public void OpportunitiesConsumeConsecutivePointsAcrossColdOddAndWrappedSchedules()
    {
        EnsureShaderTestAvailable();var queries=new List<uint>();var expected=new List<(uint Index,bool Selected)>();
        foreach(var size in new[]{(2,2),(3,3),(1,1)})foreach(uint rank in new uint[]{0,1,2,3})
        foreach(uint epoch in new uint[]{0,uint.MaxValue-2})
        for(int y=0;y<size.Item2;y++)for(int x=0;x<size.Item1;x++)
        {
            uint observation=0;
            for(uint elapsed=0;elapsed<=20;elapsed++)
            {
                uint frame=unchecked(epoch+elapsed);int phase=(int)((rank+frame%4)%4);
                bool selected=x==Math.Min(x/2*2+(phase&1),size.Item1-1)&&
                    y==Math.Min(y/2*2+(phase>>1),size.Item2-1);
                // Cold full-grid rays consume point zero. Subsequent scheduled rays advance
                // exactly once, while carry/fallback frames consume no point, even at odd edges.
                if(elapsed>0&&selected)observation++;
                Add(rank,x,y,size.Item1,size.Item2,frame,epoch,observation,selected);
            }
        }
        // A complete tile's same-phase opportunity repeats the jitter only after 256 actual
        // observations. uint frame wrap above is independent of this sequence modulo wrap.
        Add(0,0,0,2,2,0,0,0,true);Add(0,0,0,2,2,1024,0,256,true);
        int offsetCases=expected.Count;
        foreach(int size in new[]{960,1920,961,1921})for(uint point=0;point<256;point++)
            Add(0,size-1,size-1,size,size,point*4,0,point*(size%2==0?1u:4u),true,normalized:true);
        using var input=GpuShaderStorageBuffer.Create();input.UploadData(queries.ToArray());
        using var output=GpuShaderStorageBuffer.Create();output.UploadData(new Vector4[expected.Count]);
        using var points=PmjJitterTexture.Create(256,0xa5b35705u);
        using var probe=CloudSamplingComputeShader.Create(Programs.Api);
        probe.Queries=input;probe.Results=output;probe.PmjJitter=points;probe.Dispatch((expected.Count+63)/64);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit);
        using var read=output.MapScope<Vector4>(0,expected.Count,MapBufferAccessMask.MapReadBit);
        for(int i=0;i<expected.Count;i++)
        {
            Vector4 actual=read.Span[i];Assert.Equal(expected[i].Index,(uint)actual.X);
            if(i<offsetCases)Assert.Equal(expected[i].Selected?1:0,actual.W);
            float lower=i<offsetCases?-.5f:0;float upper=i<offsetCases?.5f:1;
            Assert.True(actual.Y>lower&&actual.Y<upper);Assert.True(actual.Z>lower&&actual.Z<upper);
        }
        Assert.Equal(read.Span[offsetCases-2],read.Span[offsetCases-1] with{X=0});
        Assert.NotEqual(read.Span[0].Y,read.Span[21].Y);

        // Keep authored queries local to this fact; there is no second CPU jitter implementation.
        /// <summary>Appends one independently specified schedule observation and its expected ordinal.</summary>
        void Add(uint rank,int x,int y,int width,int height,uint frame,uint epoch,uint index,bool selected,bool normalized=false)
        {
            queries.AddRange([rank,(uint)x,(uint)y,(uint)width,(uint)height,frame,epoch,normalized?1u:0u]);
            expected.Add((index,selected));
        }
    }
    #endregion
}
