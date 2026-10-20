using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VolumeStorage=VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks bounded fair queues, retained demand and successful-only lighting completion through real GPU programs.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeWorkTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Queued jobs retain age, honor source independence and cannot become fresh after failed work.</summary>
    [Fact]
    public void WorkSelectionIsFairAndCompletionRequiresSuccessfulUnchangedInputs()
    {
        EnsureShaderTestAvailable();
        using var storage=new VolumeStorage(()=>CloudVolumeMaintenanceProgram.Create(Programs.Api),()=>CloudVolumeCopyProgram.Create(Programs.Api));
        var domain=new CloudVolumeDomain(5,default,-100,100);
        storage.Request(domain,new(new object(),1,1),new(Vector3.One),Vector3.Zero,()=>true);
        while(!storage.InitializeNext(()=>true)){}
        Assert.True(storage.PublishPending(()=>true,()=>true));var fields=storage.Published;
        using var work=new CloudVolumeWork(op=>CloudVolumeWorkProgram.Create(Programs.Api,op));
        var lighting=new AtmosphereLighting(Vector3.UnitY,Vector3.One,Vector3.One,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
            {Moon=Vector3.UnitX,Lunar=Vector3.One};
        var planes=Enumerable.Repeat(new Vector4(0,0,0,1),6).ToArray();
        Assert.True(work.Select(storage,lighting,0,planes,()=>true),work.Failure);
        Assert.Equal(0u,Read(fields.Scratch,16,1)[0]);Assert.Equal(0u,Read(fields.Scratch,17,1)[0]);
        Assert.Equal(0u,Read(fields.Regions,6,1)[0]);Assert.Equal(0u,Read(fields.Regions,13,1)[0]);
        // Empty blocks keep uncompleted demand; becoming occupied must make it eligible again.
        fields.Occupancy.UploadData(Enumerable.Repeat(1u,domain.Blocks).ToArray());
        var directSeen=new HashSet<uint>();var diffuseSeen=new HashSet<uint>();
        for(uint frame=1;frame<=5;frame++)
        {
            Assert.True(work.Select(storage,lighting,frame,planes,()=>true),work.Failure);
            var header=Read(fields.Scratch,16,8);
            Assert.InRange(header[0],0u,64u);Assert.InRange(header[1],0u,32u);
            if(frame==1)
            {Assert.Equal(64u,header[0]);Assert.Equal(32u,header[1]);Assert.Equal(0u,Read(fields.Regions,6,1)[0]);}
            foreach(uint job in Read(fields.Scratch,CloudVolumeWork.DirectListWord,(int)header[0]))Assert.True(directSeen.Add(job));
            foreach(uint job in Read(fields.Scratch,CloudVolumeWork.DiffuseListWord,(int)header[1]))diffuseSeen.Add(job);
            Assert.True(work.Complete(storage,frame,3,()=>true,()=>true),work.Failure);
            Assert.True(work.Complete(storage,frame,4,()=>true,()=>true),work.Failure);
        }
        Assert.Equal(domain.Blocks*2,directSeen.Count);Assert.Equal(domain.Blocks,diffuseSeen.Count);
        var density=fields.Density;
        lighting=lighting with{Environment=new Vector3(2)};
        Assert.True(work.Select(storage,lighting,6,planes,()=>true),work.Failure);
        Assert.Equal(0u,Read(fields.Scratch,16,1)[0]);
        Assert.True(work.Complete(storage,6,4,()=>true,()=>true),work.Failure);
        lighting=lighting with{Extinction=new Vector3(.1f),Horizon=new Vector3(5)};
        Assert.True(work.Select(storage,lighting,7,planes,()=>true),work.Failure);
        Assert.Equal(0u,Read(fields.Scratch,16,1)[0]);Assert.Same(density,fields.Density);
        lighting=lighting with{Sun=Vector3.Normalize(new Vector3(1,1,0))};
        Assert.True(work.Select(storage,lighting,8,planes,()=>true),work.Failure);
        Assert.All(Read(fields.Scratch,CloudVolumeWork.DirectListWord,(int)Read(fields.Scratch,16,1)[0]),job=>Assert.Equal(0u,job%2));
        Assert.Equal(8u,Read(fields.Regions,9,1)[0]);
        lighting=lighting with{Sun=Vector3.UnitY};
        Assert.True(work.Select(storage,lighting,9,planes,()=>true),work.Failure);
        Assert.Equal(8u,Read(fields.Regions,9,1)[0]);
        using(var flag=fields.Scratch.MapScope<uint>(6*4,1,MapBufferAccessMask.MapWriteBit))flag.Span[0]=1;
        Assert.True(work.Select(storage,lighting,10,planes,()=>true),work.Failure);
        Assert.Contains(Read(fields.Scratch,CloudVolumeWork.DirectListWord,64),job=>job%2==1u);
        lighting=lighting with{Lunar=Vector3.Zero};
        Assert.True(work.Select(storage,lighting,11,planes,()=>true),work.Failure);
        Assert.All(Read(fields.Scratch,CloudVolumeWork.DirectListWord,(int)Read(fields.Scratch,16,1)[0]),job=>Assert.Equal(0u,job%2));
        lighting=lighting with{Lunar=Vector3.One};
        // Carry density failure across ordered scratch reuse; finite substituted density
        // must not allow later lighting consumers to certify failed geometry as successful.
        using(var densityFailure=fields.Scratch.MapScope<uint>(31*4,1,MapBufferAccessMask.MapWriteBit))densityFailure.Span[0]=8;
        Assert.True(work.Select(storage,lighting,12,planes,()=>true,densityPending:true),work.Failure);
        Assert.True(work.TryBatch(storage,out var batch));Assert.Equal(2,batch!.NewlyActive);
        Assert.Equal(0u,Read(fields.Regions,8,1)[0]);
        uint stamp=Read(fields.Regions,10,1)[0];
        Assert.False(work.Complete(storage,12,1,()=>true,()=>true));
        Assert.False(storage.Ready);Assert.Equal(stamp,Read(fields.Regions,10,1)[0]);
    }
    #endregion
    #region Private
    /// <summary>Inspects only bounded exact metadata through the existing buffer abstraction.</summary>
    private static uint[] Read(GpuShaderStorageBuffer buffer,int word,int count)
    {
        if(count==0)return [];
        using var view=buffer.MapScope<uint>(word*4,count,MapBufferAccessMask.MapReadBit);return view.Span.ToArray();
    }
    #endregion
}
