using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual GPU observation, retained backlog, channel completion and bounded fair selection.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudLightingVolumeWorkTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Small changes retain history, cumulative hard changes invalidate locally and native shifts preserve circular identity.</summary>
    [Fact]
    public void ObservationRetainsCompatibleHistoryAndRejectsFailedPublication()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var work=new LightingVolumeWork(op=>LightingVolumeWorkProgram.Create(Programs.Api,op));
        using var map=DynamicTexture2D.Create(20,20,PixelInternalFormat.Rgba32f);
        var placement=new CloudPlacement(-21,-41,0,0,0,0,0,0,Vector3.Zero,0);
        var input=new CloudMapInput(new object(),1,1,1,1,1,1,new(map.TextureId,2,3,4,20,50,5),placement);
        var lighting=new AtmosphereLighting(Vector3.UnitY,new Vector3(10),new Vector3(2),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty);
        var planes=new Vector4[6]; // An unbounded authored frustum includes every region.
        FillMap(map,placement,.5f);storage.Ensure(input,LightingVolumeEncoding.Create(100),()=>true);
        Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));
        var first=Read(storage.Scratch!,16);
        Assert.InRange(first[4],1u,512u);Assert.InRange(first[5],1u,256u);
        PublishAuthoredNodes(storage);
        Assert.True(work.Complete(storage,1,7,()=>true));
        uint[] completed=Read(storage.Regions,storage.Domain!.Regions*16);
        int region=Enumerable.Range(0,storage.Domain.Regions).First(r=>completed[r*16+2]==1);

        input=input with {Frame=2,Generation=2};
        Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));
        Assert.Equal(0u,Read(storage.Scratch!,16)[4]);
        Assert.Equal(15,Node(storage,2,16,2).W);
        var retainedRgb=Node(storage,2,16,2);
        lighting=lighting with {Solar=new Vector3(20)};input=input with {Frame=3,Generation=3};
        var hidden=new Vector4[6];hidden[0]=new(1,0,0,-1000000);
        Assert.True(work.Update(storage,input,map,lighting,hidden,()=>true));
        Assert.Equal(0u,Read(storage.Scratch!,16)[4]);Assert.Equal(retainedRgb,Node(storage,2,16,2));
        uint[] hiddenRegions=Read(storage.Regions,storage.Domain.Regions*16);
        Assert.All(Enumerable.Range(0,storage.Domain.Regions),r=>Assert.Equal(0u,hiddenRegions[r*16+1]&4096u));
        foreach(var (frame,density) in new[]{(4u,.55f),(5u,.6f),(6u,.7f)})
        {
            FillMap(map,placement,density);input=input with {Frame=frame,Generation=frame};
            Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));
            uint[] words=Read(storage.Regions,storage.Domain.Regions*16);
            Assert.Equal(4u,words[region*16+5]);Assert.Equal(1u,words[region*16+2]);
            Assert.Equal(frame==6?1f:15f,Node(storage,2,16,2).W);
        }
        Assert.Equal(.014f,storage.HardReference!.ReadPixelsRegion(1,1,1,1)[0],6);
        // Direction demand retains stale transmission; a newly active source clears only its own channel.
        PublishAuthoredNodes(storage);lighting=lighting with {Sun=Vector3.UnitX};input=input with {Frame=7};
        Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));Assert.Equal(15,Node(storage,2,16,2).W);
        lighting=lighting with {Lunar=Vector3.One};input=input with {Frame=8};
        Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));Assert.Equal(11,Node(storage,2,16,2).W);
        uint[] activated=Read(storage.Scratch!,576);
        Assert.True(activated.Skip(64).Take((int)activated[4]).Any(job=>(job&1u)!=0u));
        Assert.True(work.Complete(storage,8,1,()=>true));
        uint oldGeneration=storage.Generation;
        placement=placement with {CenterX=-20};input=input with {Placement=placement,Frame=9};
        FillMap(map,placement,.7f);storage.Ensure(input,storage.Encoding,()=>true);
        Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));Assert.Equal(oldGeneration,storage.Generation);
        Assert.Equal(.014f,storage.HardReference.ReadPixelsRegion(1,1,1,1)[0],6);
        uint[] shifted=Read(storage.Regions,storage.Domain!.Regions*16);
        Assert.Contains(8u,Enumerable.Range(0,storage.Domain.Regions).Select(r=>shifted[r*16+2]));
        uint[] beforeMissing=Read(storage.Regions,storage.Domain.Regions*16);
        Assert.False(work.Update(storage,null,null,lighting,planes,()=>false));
        Assert.Equal(beforeMissing,Read(storage.Regions,storage.Domain.Regions*16));
        int certificates=0;input=input with {Frame=10};
        Assert.False(work.Update(storage,input,map,lighting,planes,()=>++certificates==1));
        Assert.False(work.Ready);Assert.False(storage.Ready);Assert.NotNull(work.Failure);
        work.Reset();storage.Ensure(input,LightingVolumeEncoding.Create(100),()=>true);
        Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));
        uint beforeRollover=storage.Generation;input=input with {Frame=0};
        Assert.True(work.Update(storage,input,map,lighting,planes,()=>true));Assert.True(storage.Generation>beforeRollover);
    }

    /// <summary>Maximum-domain continuous demand respects both budgets, oldest-first priority and the 43-frame fair revisit bound.</summary>
    [Fact]
    public void SelectionIsBoundedAndNonstarvingAtMaximumDomain()
    {
        EnsureShaderTestAvailable();
        using var storage=new LightingVolumeStorage(()=>LightingVolumeMaintenanceProgram.Create(Programs.Api));
        using var merge=LightingVolumeWorkProgram.Create(Programs.Api,2);
        using var select=LightingVolumeWorkProgram.Create(Programs.Api,4);
        var input=new CloudMapInput(new object(),1,1,1,1,1,200,new(1,2,3,4,200,50,5),default);
        storage.Ensure(input,LightingVolumeEncoding.Create(100),()=>true);
        var domain=storage.Domain!;
        uint[] records=new uint[domain.Regions*16];
        for(int r=0;r<domain.Regions;r++)
        {
            records[r*16]=storage.Generation;records[r*16+1]=7;
            for(int channel=0;channel<3;channel++) {records[r*16+2+channel]=100;records[r*16+5+channel]=(uint)(100+r%7);}
            // A small far-end incompatible bucket must precede older visible background work.
            if(r>domain.Regions-20) records[r*16+1]|=0x70;
        }
        storage.Regions.UploadData(records);
        var directSeen=new HashSet<uint>();var diffuseSeen=new HashSet<uint>();uint[]? activeRecords=null;
        var planes=new Vector4[6];
        for(uint iteration=0;iteration<43;iteration++)
        {
            foreach(var program in new[]{merge,select})
            {
                program.Generation=storage.Generation;
                program.Capture(domain,input with {Frame=200+iteration},0,0,false,null,1,3,0,0,false,true,0,planes);
                program.Regions=storage.Regions;program.Scratch=storage.Scratch;
            }
            merge.Dispatch((domain.Regions+63)/64);Barrier();
            if(iteration==0) activeRecords=Read(storage.Regions,records.Length);
            select.Dispatch(1);Barrier();
            uint[] list=Read(storage.Scratch!,832);
            Assert.Equal(512u,list[4]);Assert.Equal(256u,list[5]);
            uint[] direct=list.Skip(64).Take(512).ToArray(),diffuse=list.Skip(576).Take(256).ToArray();
            Assert.Equal(512,direct.Distinct().Count());Assert.Equal(256,diffuse.Distinct().Count());
            foreach(uint job in direct.Take(256)) directSeen.Add(job);
            foreach(uint job in diffuse.Take(128)) diffuseSeen.Add(job);
            if(iteration==0)
            {
                var fair=direct.Take(256).ToHashSet();
                var expected=Enumerable.Range(0,domain.Regions*2).Select(job=>(uint)job)
                    .Where(job=>(activeRecords![(job/2)*16+1]&3)!=0&&!fair.Contains(job))
                    .OrderBy(job=>(activeRecords![(job/2)*16+1]&(1u<<(4+(int)(job%2))))!=0?0:2)
                    .ThenBy(job=>activeRecords![(job/2)*16+5+job%2]).ThenBy(job=>job).Take(256).OrderBy(job=>job);
                Assert.Equal(expected,direct.Skip(256).OrderBy(job=>job));
            }
        }
        uint[] final=Read(storage.Regions,records.Length);
        for(int r=0;r<domain.Regions;r++) if((activeRecords![r*16+1]&7)!=0)
        {
            Assert.Contains((uint)(r*2),directSeen);Assert.Contains((uint)(r*2+1),directSeen);Assert.Contains((uint)r,diffuseSeen);
            Assert.Equal(100u,final[r*16+2]);Assert.Equal(records[r*16+5],final[r*16+5]);
        }
    }
    #endregion

    #region Private
    /// <summary>Authors native descriptors by stable material column key across negative-window recentering.</summary>
    private static void FillMap(DynamicTexture2D map,CloudPlacement placement,float changed)
    {
        var data=new float[map.Width*map.Width*4];
        for(int z=0;z<map.Width;z++) for(int x=0;x<map.Width;x++)
        {
            int i=(z*map.Width+x)*4;
            data[i]=(placement.CenterX-placement.WindTileX+x==-19 && placement.CenterZ-placement.WindTileZ+z==-39)?changed:.5f;
            data[i+2]=-1;data[i+3]=1;
        }
        map.UploadDataImmediate(data);
    }
    /// <summary>Publishes finite synthetic lighting before exercising the solver-completion metadata boundary.</summary>
    private static void PublishAuthoredNodes(LightingVolumeStorage storage)
    {
        var domain=storage.Domain!;var data=new float[domain.Voxels*4];
        for(int i=0;i<domain.Voxels;i++) {data[i*4]=3;data[i*4+1]=4;data[i*4+2]=5;data[i*4+3]=15;}
        storage.Diffuse.UploadDataImmediate(data,0,0,0,domain.Size,34,domain.Size);Barrier();
    }
    /// <summary>Reads one node through an ordinary framebuffer-backed texture readback owner.</summary>
    private static Vector4 Node(LightingVolumeStorage storage,int x,int y,int z)
    {
        // Layer attachments use the actual framebuffer abstraction, with no raw GL test calls.
        using var target=DynamicGpuFramebuffer.CreateEmpty("Cloud.LightingVolume.Node");
        using var draw=StateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer);
        using var read=StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer);
        using var staging=GpuPixelPackBuffer.Create();staging.AllocateOrphan(16);
        using var image=GpuFramebufferAttachment.FromTexture(storage.Diffuse,layer:z);
        target.SetAttachment(FramebufferAttachment.ColorAttachment0,image);target.Bind();
        staging.ReadPixels(x,y,1,1,PixelFormat.Rgba,PixelType.Float,0);
        using var pixels=staging.MapRange<float>(0,4,MapBufferAccessMask.MapReadBit);
        return new(pixels.Span[0],pixels.Span[1],pixels.Span[2],pixels.Span[3]);
    }
    /// <summary>Reads an exact metadata slice with the existing storage-buffer mapping scope.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer,int count)
    {
        Barrier();using var mapped=buffer.MapScope<uint>(0,count,MapBufferAccessMask.MapReadBit);return mapped.Span.ToArray();
    }
    /// <summary>Separates fixture authoring, compute consumers and exact diagnostic readback.</summary>
    private static void Barrier()=>GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.ShaderStorageBarrierBit|MemoryBarrierFlags.BufferUpdateBarrierBit|
        MemoryBarrierFlags.ShaderImageAccessBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit|MemoryBarrierFlags.TextureUpdateBarrierBit);
    #endregion
}
