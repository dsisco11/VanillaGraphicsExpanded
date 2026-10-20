using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact global queue selection across the maximum domain without allocating density/lighting textures.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeWorkReductionTests(HeadlessGLFixture fixture,Xunit.ITestOutputHelper output):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Hierarchical winners match an independent CPU ordering, including cursor wrap, priorities and retained ages.</summary>
    [Fact]
    public void MaximumDomainSelectionPreservesExactGlobalWinners()
    {
        EnsureShaderTestAvailable();
        var d=new CloudVolumeDomain(200,default,-100,100);var data=new uint[d.Blocks*16];
        for(int r=0;r<d.Blocks;r++)
        {
            int b=r*16;data[b]=6;data[b+5]=data[b+7]=data[b+12]=5;
            data[b+6]=r%29==0?0u:(r%5==0?5u:4u);data[b+8]=r%31==0?0u:(r%11==0?5u:4u);
            data[b+9]=(uint)(50+(r*17)%256);data[b+13]=r%23==0?0u:4u;data[b+14]=(uint)(40+(r*13)%128);
        }
        var header=new uint[(CloudVolumeDomain.ScratchBytes-4096)/4];header[18]=(uint)(d.Blocks*2-9);header[19]=(uint)(d.Blocks-7);header[^1]=0xdeadbeef;
        using var regions=GpuShaderStorageBuffer.Create();using var scratch=GpuShaderStorageBuffer.Create();using var commands=GpuShaderStorageBuffer.Create();
        regions.UploadData(data);scratch.UploadData(header);commands.UploadData(new uint[12]);
        using var occupancy=GpuShaderStorageBuffer.Create();occupancy.UploadData(Enumerable.Repeat(1u,d.Blocks).ToArray());
        var programs=Enumerable.Range(0,6).Select(op=>CloudVolumeWorkProgram.Create(Programs.Api,op)).ToArray();
        try
        {
            foreach(var p in programs){p.Regions=regions;p.Scratch=scratch;p.Commands=new(commands,0,48);p.Occupancy=occupancy;}
            var planes=Enumerable.Repeat(new Vector4(0,0,0,1),6).ToArray();planes[0]=new(1,0,0,-2500);
            using var timer=GpuTimerQuery.Create("Tests.Cloud.Work.Maximum");var times=new List<double>();
            for(int sample=0;sample<4;sample++)
            {
                regions.UploadData(data);scratch.UploadData(header);
                timer.Begin();
                void Submit(int op,int count,int source=0,int destination=0,int dg=0,int ig=0)
                {
                    programs[op].Capture(d,1000,20,3,0,0,true,0,planes,source,destination,dg,ig);
                    programs[op].Dispatch(count);
                    GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit|MemoryBarrierFlags.CommandBarrierBit);
                }
                Submit(0,1);Submit(1,(d.Blocks+255)/256);
                int direct=(d.Blocks*2+255)/256,diffuse=(d.Blocks+255)/256,source=CloudVolumeWork.SummaryA,destination=CloudVolumeWork.SummaryB;
                Submit(2,direct+diffuse,0,source,direct,diffuse);
                while(direct>1||diffuse>1)
                {
                    Submit(3,(direct+3)/4+(diffuse+3)/4,source,destination,direct,diffuse);
                    direct=(direct+3)/4;diffuse=(diffuse+3)/4;(source,destination)=(destination,source);
                }
                Submit(4,1,source,0,direct,diffuse);timer.End();
                double milliseconds=timer.GetResultNanoseconds()/1e6;if(sample>0)times.Add(milliseconds);
            }
            var actual=Read(scratch,0,32);
            for(int kind=0;kind<2;kind++)
            {
                var pending=Enumerable.Range(0,d.Blocks*(kind==0?2:1)).Where(job=>
                    kind==1||data[(job/2)*16+6+(job%2)*2]!=5u).ToArray();
                int budget=kind==0?64:32,total=d.Blocks*(kind==0?2:1);uint cursor=header[18+kind];
                var fair=pending.OrderBy(job=>(job+total-(int)cursor)%total).Take(budget/2).ToArray();
                var rest=pending.Except(fair).OrderBy(job=>Priority(d,data,job,kind)).ThenBy(job=>data[(kind==0?job/2:job)*16+(kind==0?9:14)]).ThenBy(job=>job).Take(budget-fair.Length);
                var expected=fair.Concat(rest).Select(job=>(uint)job).ToArray();
                Assert.Equal((uint)expected.Length,actual[16+kind]);Assert.Equal((uint)pending.Length,actual[22+kind]);
                Assert.Equal(expected,Read(scratch,kind==0?CloudVolumeWork.DirectListWord:CloudVolumeWork.DiffuseListWord,expected.Length));
                Assert.Equal((uint)((fair[^1]+1)%total),actual[18+kind]);
                if(kind==0)for(int sourceIndex=0;sourceIndex<2;sourceIndex++)
                {
                    var source=expected.Where(job=>job%2==sourceIndex).Select(job=>job/2).ToArray();
                    Assert.Equal((uint)source.Length,actual[29+sourceIndex]);
                    Assert.Equal(source,Read(scratch,16512+sourceIndex*64,source.Length));
                }
            }
            Assert.Equal(0xdeadbeefu,Read(scratch,header.Length-1,1)[0]);
            var retained=Read(regions,0,data.Length);
            for(int b=0;b<data.Length;b+=16){Assert.Equal(data[b+6],retained[b+6]);Assert.Equal(data[b+8],retained[b+8]);Assert.Equal(data[b+13],retained[b+13]);}
            var args=Read(commands,0,12);Assert.Equal(new uint[]{64,1,1,32,CloudVolumeDiffuseProgram.DefaultSlices,1},args.Take(6));
            Assert.Equal(64u,args[6]+args[9]);Assert.Equal(8u/CloudVolumeDirectProgram.DefaultWidth,args[7]);Assert.Equal(args[7],args[10]);
            times.Sort();output.WriteLine($"Maximum-domain scheduler: {d.Blocks} blocks, median {times[1]:F4} ms GPU; warm samples {string.Join(", ",times.Select(t=>t.ToString("F4")))} ms. Metadata-only standalone measurement; no live frame-time claim.");
        }
        finally{foreach(var p in programs)p.Dispose();}
    }
    #endregion
    #region Private
    /// <summary>Independently orders completed identity then the positive-X native frustum plane used by this scene.</summary>
    private static int Priority(CloudVolumeDomain d,uint[] records,int job,int kind)
    {
        int region=kind==0?job/2:job,b=region*16;
        if(records[b+(kind==0?6+job%2*2:13)]==0)return 0;
        int px=(region%d.BlocksX)*8,pz=(region/(d.BlocksX*d.BlocksY))*8;
        int qx=(px-d.SlotX+d.Size)%d.Size,qz=(pz-d.SlotZ+d.Size)%d.Size;
        int wx=Math.Min(8,d.Size-px),wz=Math.Min(8,d.Size-pz);
        bool seam=qx+wx>d.Size||qz+wz>d.Size;
        double hi=d.FirstX*CloudVolumeDomain.HorizontalCell-d.AnchorX+(qx+wx)*CloudVolumeDomain.HorizontalCell;
        return seam||hi>=2500?2:3;
    }
    /// <summary>Reads bounded exact metadata through the existing buffer owner.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer,int offset,int count)
    {using var view=buffer.MapScope<uint>(offset*4,count,MapBufferAccessMask.MapReadBit);return view.Span.ToArray();}
    #endregion
}
