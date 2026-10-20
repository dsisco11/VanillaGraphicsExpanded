using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using CloudVolumeStorage = VanillaGraphicsExpanded.PBR.Clouds.Volume.CloudVolumeStorage;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual circular hardware sampling, retained fields, atomic replacement and failed publication.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudVolumeStorageTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Initialized fields retain seam neighbors and publish scale only with a completed destination.</summary>
    [Fact]
    public void CircularFieldsPreserveOverlapAndPublishCoherently()
    {
        EnsureShaderTestAvailable();
        var submission=new CloudVolumeSubmission();
        using var storage = new CloudVolumeStorage(() => CloudVolumeMaintenanceProgram.Create(Programs.Api), () => CloudVolumeCopyProgram.Create(Programs.Api),submission);
        using var probe = CloudVolumeProbeProgram.Create(Programs.Api);
        using var output = GpuShaderStorageBuffer.Create(); output.UploadData(new Vector4[2]);
        var identity = new CloudVolumeIdentity(new object(), 1, 1);
        var encoding = new CloudVolumeEncoding(new Vector3(1000));
        var domain = new CloudVolumeDomain(5, default, -100, 100);
        storage.Request(domain, identity, encoding, new Vector3(4), () => true);
        Assert.False(storage.Ready); Assert.False(storage.PublishPending(() => false, () => true));
        while (!storage.InitializeNext(() => true)) { }
        Assert.False(storage.Ready); Assert.True(storage.PublishPending(() => true, () => true));
        var fields = storage.Published;
        Assert.Equal(PixelInternalFormat.R16f, fields.Density.InternalFormat);
        Assert.Equal(PixelInternalFormat.Rg16f, fields.Direct.InternalFormat);
        Assert.Equal(TextureFilterMode.Linear, fields.Diffuse.FilterMode);
        Assert.Equal(TextureTarget.Texture3D, fields.Density.TextureTarget);
        Assert.Equal(1, fields.Density.StorageMipLevels);
        var first = Query(probe, fields, output, new Vector3(125, 0, 125));
        Assert.Equal(Vector4.Zero with {Y=1, Z=1}, first[0]); Assert.Equal(new Vector4(4,4,4,0), first[1]);

        // Author cells immediately on each side of the physical X seam, well inside finite coverage.
        int z = CloudVolumeDomain.Mod(0, domain.Size), y = 32;
        fields.Density.UploadDataImmediate(new[]{2f}, domain.Size-1,y,z,1,1,1);
        fields.Density.UploadDataImmediate(new[]{6f}, 0,y,z,1,1,1);
        float nativeX = (float)-domain.AnchorX;
        float nativeY = (float)(domain.Bottom + (y-.5)*domain.VerticalCell);
        float nativeZ = (float)(.5*CloudVolumeDomain.HorizontalCell-domain.AnchorZ);
        Vector3 point = new(nativeX,nativeY,nativeZ);
        Assert.Equal(4, Query(probe,fields,output,point)[0].X);
        var density = fields.Density;
        var metadata=new uint[domain.Blocks*16];
        metadata[1]=10;metadata[3]=123;metadata[5]=11;metadata[7]=12;
        metadata[9]=234;metadata[12]=13;metadata[14]=345;metadata[2]=9;metadata[6]=9;metadata[13]=9;
        fields.Regions.UploadData(metadata);
        var moved = new CloudVolumeDomain(5, new CloudPlacement(1,0,0,0,0,0,0,0,Vector3.Zero,0), -100,100,domain);
        storage.Request(moved, identity, encoding, new Vector3(4), () => true);
        Assert.Same(density,storage.Published.Density);
        using(var words=fields.Regions.MapScope<uint>(0,16,MapBufferAccessMask.MapReadBit))
        {
            Assert.Equal(123u,words.Span[3]);Assert.Equal(234u,words.Span[9]);Assert.Equal(345u,words.Span[14]);
            Assert.Equal(10u,words.Span[1]);Assert.Equal(11u,words.Span[5]);Assert.Equal(12u,words.Span[7]);Assert.Equal(13u,words.Span[12]);
            Assert.Equal(0u,words.Span[2]);Assert.Equal(0u,words.Span[6]);Assert.Equal(0u,words.Span[13]);
        }
        Assert.Equal(4, Query(probe,fields,output,point-new Vector3(50,0,0))[0].X);
        int guardX = moved.SlotX;
        var guardPoint = new Vector3((float)((moved.FirstX+.5)*CloudVolumeDomain.HorizontalCell-moved.AnchorX),nativeY,nativeZ);
        Assert.Equal(0,Query(probe,fields,output,guardPoint)[0].X);

        var oldDiffuse = fields.Diffuse;
        var grown = new CloudVolumeEncoding(new Vector3(1e8f));
        var cache=StateCache.Current;
        cache.BindTexture(TextureTarget.Texture3D,0,fields.Density.TextureId);
        cache.BindImageTexture(0,fields.Density.TextureId,0,true,0,TextureAccess.ReadOnly,SizedInternalFormat.R16f);
        fields.Regions.BindBase(0);
        int boundaries=submission.Boundaries,dispatches=submission.Dispatches,barriers=submission.Barriers;
        submission.Batch(storage.SubmissionPrograms,()=>
        {
            Assert.True(storage.UpdateDiffuse(grown,()=>true,_=>{}));
            Assert.Throws<InvalidOperationException>(()=>submission.Batch(storage.SubmissionPrograms,()=>{}));
            Assert.Throws<InvalidOperationException>(()=>submission.Run(probe,()=>probe.Dispatch(1)));
        });
        Assert.Equal(1,submission.Boundaries-boundaries);Assert.Equal(1,submission.Dispatches-dispatches);
        Assert.Equal(2,submission.Barriers-barriers);Restored();
        Assert.NotSame(oldDiffuse,fields.Diffuse); Assert.Equal(grown,fields.Encoding);
        Assert.Equal(new Vector4(4,4,4,0),Query(probe,fields,output,point-new Vector3(50,0,0))[1]);
        var replacement = new CloudVolumeDomain(5, default,-1000,1000,moved);
        storage.Request(replacement,identity,grown,Vector3.One,()=>true);
        Assert.Same(fields,storage.Published); Assert.Equal(fields.Domain.Bytes+replacement.Bytes,storage.StorageBytes);
        while(!storage.InitializeNext(()=>true)) { }
        Assert.False(storage.PublishPending(()=>false,()=>true)); Assert.Same(fields,storage.Published);
        Assert.True(storage.PublishPending(()=>true,()=>true)); Assert.False(density.IsValid);
        fields=storage.Published;
        cache.BindTexture(TextureTarget.Texture3D,0,fields.Density.TextureId);
        cache.BindImageTexture(0,fields.Density.TextureId,0,true,0,TextureAccess.ReadOnly,SizedInternalFormat.R16f);
        fields.Regions.BindBase(0);
        var stable=fields.Diffuse; var stableEncoding=fields.Encoding; int certificates=0;
        var failure=Assert.Throws<InvalidOperationException>(()=>submission.Batch(storage.SubmissionPrograms,()=>
        {
            Assert.False(storage.UpdateDiffuse(grown,()=>++certificates==1,_=>{}));
            throw new InvalidOperationException("Injected update failure");
        }));
        Assert.Equal("Injected update failure",failure.Message);Restored();
        Assert.False(storage.Ready); Assert.Same(stable,fields.Diffuse); Assert.Equal(stableEncoding,fields.Encoding);
        storage.Reset(); Assert.False(stable.IsValid); Assert.Null(storage.Pending); Assert.Equal(0,storage.StorageBytes);
        storage.Request(domain,identity,encoding,Vector3.One,()=>true);
        var partial=storage.Pending!.Density;
        Assert.Throws<InvalidOperationException>(()=>storage.InitializeNext(()=>false));
        Assert.False(partial.IsValid); Assert.Null(storage.Pending);

        /// <summary>Checks borrowed image, sampler and indexed bindings after successful and failed batch exit.</summary>
        void Restored()
        {
            Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture3D,0,out int texture));Assert.Equal(fields.Density.TextureId,texture);
            Assert.True(cache.TryGetCachedImageTexture(0,out int image));Assert.Equal(fields.Density.TextureId,image);
            Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.ShaderStorageBuffer,0,out int buffer,out _,out _));
            Assert.Equal(fields.Regions.BufferId,buffer);
        }
    }
    #endregion
    #region Private
    /// <summary>Samples three real textures and reads only two bounded diagnostic records through the buffer owner.</summary>
    private static Vector4[] Query(CloudVolumeProbeProgram probe,CloudVolumeFields fields,GpuShaderStorageBuffer output,Vector3 point)
    {
        probe.Capture(fields,point); probe.Density=fields.Density; probe.Direct=fields.Direct;
        probe.Diffuse=fields.Diffuse; probe.Result=output; probe.Dispatch(1);
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.BufferUpdateBarrierBit|MemoryBarrierFlags.ShaderStorageBarrierBit);
        using var read=output.MapScope<Vector4>(0,2,MapBufferAccessMask.MapReadBit); return read.Span.ToArray();
    }
    #endregion
}
