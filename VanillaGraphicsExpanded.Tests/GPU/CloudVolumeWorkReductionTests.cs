using VanillaGraphicsExpanded.Rendering.RHI;
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
    [Theory]
    [InlineData(2560)][InlineData(81920)][InlineData(327680)]
    public void MaximumDomainSelectionPreservesExactGlobalWinners(int rays)
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
        using var regions=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();using var scratch=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();using var commands=RHICommandContext.Current.CreateIndirectBuffer(new RHIBufferDescription { Role = RHIBufferRole.IndirectDraw, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();
        regions.UploadData(data);scratch.UploadData(header);commands.UploadData(new uint[45]);
        using var occupancy=RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = RHIBufferRole.ShaderStorage, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.DynamicDraw, Label = null }).RequireResource();
        using var inputs=new PackedUniformBuffer(208);var submission=new CloudVolumeSubmission();
        var programs=Enumerable.Range(0,11).Select(op=>CloudVolumeWorkProgram.Create(Programs.Api,op,CloudVolumeLightingBudget.Candidates(rays,true)>64,inputs)).ToArray();
        try
        {
            foreach(var p in programs){p.Regions=regions;p.Scratch=scratch;p.Commands=new(commands,0,180);p.Occupancy=occupancy;}
            var planes=Enumerable.Repeat(new Vector4(0,0,0,1),6).ToArray();planes[0]=new(1,0,0,-2500);
            // The same ordering oracle covers dense winners, sparse active groups and no eligible work.
            foreach(string scenario in new[]{"dense","sparse","partial","empty"})
            {
                var occupied=Enumerable.Range(0,d.Blocks).Select(r=>scenario=="partial"?(r%13==0?5u:5u+((uint)(r%256+1)<<3)):scenario=="dense"||
                    scenario=="sparse"&&(r%4093==0||r==d.Blocks-1)?1u:0u).ToArray();
                occupancy.UploadData(occupied);
                // Geometry membership is rebuilt once. Source-demand resets leave its retained list intact.
                header[48]=1;scratch.UploadData(header);
                using var timer=GpuTimerQuery.Create("Tests.Cloud.Work.Maximum");var times=new List<double>();
                double rebuilding=0;
                for(int sample=0;sample<4;sample++)
                {
                    regions.UploadData(data);
                    using(var cursor=scratch.MapScope<uint>(18*4,2,RHIBufferMapAccess.Write))
                    {cursor.Span[0]=header[18];cursor.Span[1]=header[19];}
                    timer.Begin();
                    /// <summary>Submits one real selector operation and orders its summary and indirect outputs.</summary>
                    void Submit(int op,int count,int source=0,int destination=0,int level=0,int indirect=-1)
                    {
                        programs[op].Capture(d,1000,20,3,0,0,true,0,planes,source,destination,lightingRays:rays,selectionLevel:level);
                        submission.Run(programs[op],()=>
                        {if(indirect>=0)programs[op].DispatchIndirect(commands,indirect);else programs[op].Dispatch(count);});
                    }
                    submission.Batch(programs,()=>
                    {
                    Submit(0,1);Submit(1,(d.Blocks+255)/256);
                    Submit(6,1);Submit(7,0,indirect:144);Submit(8,0,indirect:168);Submit(9,0,indirect:156);Submit(10,1);
                    int source=CloudVolumeWork.SummaryA,destination=CloudVolumeWork.SummaryB;
                    Submit(2,0,0,source,indirect:72);
                    for(int level=1;level<6;level++)
                    {
                        Submit(3,0,source,destination,level,indirect:(18+3*level)*4);
                        (source,destination)=(destination,source);
                    }
                    Submit(4,1,source,level:5);
                    });timer.End();
                    double milliseconds=timer.GetResultNanoseconds()/1e6;if(sample>0)times.Add(milliseconds);else rebuilding=milliseconds;
                    Assert.Equal(1u,Read(scratch,51,1)[0]);
                }
                var members=Enumerable.Range(0,d.Blocks).Where(r=>Origins(occupied[r])!=0).Select(r=>(uint)r).ToArray();
                Assert.Equal((uint)members.Length,Read(scratch,49,1)[0]);
                Assert.Equal(members,Read(scratch,970576,members.Length));
                var actual=Read(scratch,0,32);
                for(int kind=0;kind<2;kind++)
                {
                    var pending=Enumerable.Range(0,d.Blocks*(kind==0?2:1)).Where(job=>
                        Origins(occupied[kind==0?job/2:job])!=0&&(kind==1||data[(job/2)*16+6+(job%2)*2]!=5u)).ToArray();
                    int budget=rays/2560*(kind==0?2:1),total=d.Blocks*(kind==0?2:1);uint cursor=header[18+kind];
                    int candidates=CloudVolumeLightingBudget.Candidates(rays,kind==0),scale=kind==0?1:8;
                    int allowance=budget*256*scale,fairAllowance=(budget+1)/2*256*scale,used=0;
                    var chosen=new List<int>();var fair=new List<int>();
                    foreach(int job in pending.OrderBy(job=>(job+total-(int)cursor)%total).Take((candidates+1)/2))
                    {
                        int cost=(int)Origins(occupied[kind==0?job/2:job])*scale;
                        if(used+cost>fairAllowance)break;
                        fair.Add(job);chosen.Add(job);used+=cost;
                    }
                    foreach(int job in pending.OrderBy(job=>Priority(d,data,job,kind)).ThenBy(job=>data[(kind==0?job/2:job)*16+(kind==0?9:14)]).ThenBy(job=>job).Take(candidates))
                    {
                        int cost=(int)Origins(occupied[kind==0?job/2:job])*scale;
                        if(!chosen.Contains(job)&&chosen.Count<(kind==0?256:128)&&used+cost<=allowance){chosen.Add(job);used+=cost;}
                    }
                    var expected=chosen.Select(job=>(uint)job).ToArray();
                    Assert.Equal((uint)used,Read(scratch,42+kind,1)[0]);Assert.InRange(used,0,allowance);
                    Assert.Equal(expected.Length,expected.Distinct().Count());
                    Assert.Equal((uint)expected.Length,actual[16+kind]);Assert.Equal((uint)pending.Length,actual[22+kind]);
                    Assert.Equal(expected,Read(scratch,kind==0?CloudVolumeWork.DirectListWord:CloudVolumeWork.DiffuseListWord,expected.Length));
                    Assert.Equal(fair.Count==0?cursor:(uint)((fair[^1]+1)%total),actual[18+kind]);
                    if(kind==0)for(int sourceIndex=0;sourceIndex<2;sourceIndex++)
                    {
                        var source=expected.Where(job=>job%2==sourceIndex).Select(job=>job/2).ToArray();
                        Assert.Equal((uint)source.Length,actual[29+sourceIndex]);
                        Assert.Equal(source,Read(scratch,16768+sourceIndex*256,source.Length));
                    }
                }
                Assert.Equal(0xdeadbeefu,Read(scratch,header.Length-1,1)[0]);
                var retained=Read(regions,0,data.Length);
                for(int b=0;b<data.Length;b+=16)
                {
                    Assert.Equal(data[b+6],retained[b+6]);Assert.Equal(data[b+8],retained[b+8]);
                    Assert.Equal(data[b+9],retained[b+9]);Assert.Equal(data[b+13],retained[b+13]);
                    Assert.Equal(data[b+14],retained[b+14]);
                }
                var args=Read(commands,0,18);Assert.Equal(new uint[]{actual[16],1,1,actual[17],8,1},args.Take(6));
                Assert.Equal(actual[16],args[6]+args[9]);Assert.Equal(8u/CloudVolumeDirectProgram.DefaultWidth,args[7]);Assert.Equal(args[7],args[10]);
                Assert.Equal(new uint[]{actual[17]==0?0u:(uint)d.BlocksX,17,(uint)d.BlocksX,0,1,1},args.Skip(12));
                // Changing a source target dirties lighting demand, never positive-density membership.
                foreach(int op in new[]{0,1,6,7,8,9})
                {
                    programs[op].Capture(d,1001,21,3,op==0?1:0,0,true,0,planes,lightingRays:rays);
                    if(op is 7 or 8 or 9)programs[op].DispatchIndirect(commands,op==7?144:op==8?168:156);
                    else programs[op].Dispatch(op==1?(d.Blocks+255)/256:1);
                    RHICommandContext.Current.Barrier(new(RHIAccess.ShaderStorage|RHIAccess.BufferUpdate|RHIAccess.Indirect));
                }
                Assert.Equal(1u,Read(scratch,51,1)[0]);
                Assert.Equal(members,Read(scratch,970576,members.Length));
                times.Sort();output.WriteLine($"Maximum-domain scheduler {scenario}: {d.Blocks} blocks, eligible compact members {members.Length}, selected direct/diffuse {actual[16]}/{actual[17]}, backlog {actual[22]}/{actual[23]}, rebuild plus selection {rebuilding:F4} ms, stable reused selection median {times[1]:F4} ms GPU; warm samples {string.Join(", ",times.Select(t=>t.ToString("F4")))} ms. Shared208-byte inputs and one actual owning submission boundary per sample; membership generation stayed1 across source-demand resets. Metadata-only headless measurement; no live frame-time claim.");
            }
        }
        finally{foreach(var p in programs)p.Dispose();}
    }
    #endregion
    #region Private
    /// <summary>Decodes exact represented origins, conservatively reserving unknown support.</summary>
    private static uint Origins(uint value)=>(value&4u)!=0?(value>>3)&511u:(value&3u)!=0?256u:0u;
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
    private static uint[] Read(GpuBufferObject buffer,int offset,int count)
    {
        if(count==0)return [];
        using var view=buffer.MapScope<uint>(offset*4,count,RHIBufferMapAccess.Read);return view.Span.ToArray();
    }
    #endregion
}
