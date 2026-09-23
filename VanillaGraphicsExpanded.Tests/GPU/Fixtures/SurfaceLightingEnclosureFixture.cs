using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.LumOn.Scene.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Reusable closed voxel enclosure with real captured pages and two outgoing lighting generations.</summary>
internal sealed class SurfaceLightingEnclosureFixture : IDisposable
{
    private const int Edge = 8, AtlasEdge = 64, TilesPerAxis = 8;
    private readonly ScopedPbrMaterialFixture material = new();
    private readonly BinaryShaderApiFixture assets = new();
    private readonly SurfaceLightingDispatch producer;
    private readonly GpuShaderStorageBuffer work, metadata, slots, readiness;
    private readonly Texture3D depth, captured, direct, indirect, pages;
    private readonly Texture3D[] outgoing;
    private readonly LumonSceneCaptureWorkGpu[] captureItems;
    private readonly LumonSceneRelightWorkGpu[] lightingItems;
    private int generation;
    public long DependencyRevision { get; set; }
    public bool DividedRoom { get; }
    public bool DoorOpen { get; set; } = true;
    private readonly uint materialId;
    private readonly int offsetX, firstChunk, chunkCount;
    public SharedTraceGeometryFixture Geometry { get; }
    public int BlockId => material.Cube.Id;
    public int BlockLight { get; set; }
    public int SunLight { get; set; }
    public int? ExteriorBlockLight { get; set; }
    public bool EmissionPolicy { get; set; }
    public SurfaceLightingSnapshot Snapshot => new(outgoing[generation % 2], direct, indirect, pages, captured,
        metadata, slots, readiness, new(firstChunk,1,0), new(chunkCount,1,1), default, Edge, TilesPerAxis, TilesPerAxis*TilesPerAxis, generation, DependencyRevision);

    #region Setup
    /// <summary>Captures every inward facing patch of an eight-voxel enclosure.</summary>
    public SurfaceLightingEnclosureFixture(float reflectance = .25f, int blockLight = 32, float emission = 0f, int xOffset = 0, bool dividedRoom = false)
    {
        DividedRoom=dividedRoom;
        offsetX=xOffset; firstChunk=xOffset>>5; chunkCount=((xOffset+7)>>5)-firstChunk+1;
        BlockLight = blockLight;
        material.SetReadiness(true,true,new System.Numerics.Vector3(reflectance), emission / 32f);
        var materials = new TraceGeometryMaterials(); materialId = materials.Resolve(material.Cube);
        Geometry = new(TraceGeometryCoverage.Plan(new(4+offsetX,36,4),true,32,256),materials,Sample);
        Geometry.Publish();
        depth = Texture(PixelInternalFormat.R16f); captured = Texture(PixelInternalFormat.Rgba8);
        direct = Texture(PixelInternalFormat.Rgba16f); indirect = Texture(PixelInternalFormat.Rgba16f);
        outgoing = [Texture(PixelInternalFormat.Rgba16f),Texture(PixelInternalFormat.Rgba16f)];
        pages = Texture3D.Create(128,128,chunkCount,PixelInternalFormat.R32ui,textureTarget:TextureTarget.Texture2DArray);
        var entries = new uint[128*128*chunkCount]; var capture = new List<LumonSceneCaptureWorkGpu>();
        var seen=new HashSet<(uint Slot,uint Patch)>();
        foreach (var face in Enumerable.Range(0,6).Select(axis => (Axis:(uint)axis, Plane:axis%2==0?0:7))
            .Concat(dividedRoom ? new[]{(Axis:4u,Plane:5),(Axis:5u,Plane:5)} : []))
        for (int v=0; v<8; v++)
        for (int u=0; u<8; u++)
        {
            uint axis=face.Axis; int plane=face.Plane;
            int x=offsetX+(axis<2?plane:u), y=32+(axis<2?v:axis<4?plane:v), z=axis<2?u:axis<4?v:plane;
            int px=x&31, py=y&31, pz=z&31;
            int localPlane=axis<2?px:axis<4?py:pz;
            int uc=axis<2?pz:px, vc=axis<2?py:axis<4?pz:py;
            uint slot=(uint)((x>>5)-firstChunk);
            uint patch=1+6*(uint)(localPlane*64+(vc/4)*8+uc/4)+axis;
            if(!seen.Add((slot,patch))) continue;
            uint id=(uint)capture.Count+1;
            capture.Add(new(id,slot,patch,patch));
            entries[slot*128*128+patch]=LumonScenePageTableEntryPacking.Pack(id,LumonScenePageTableEntryPacking.Flags.Resident).Packed;
        }
        captureItems = capture.ToArray();
        lightingItems = captureItems.Select(item=>new LumonSceneRelightWorkGpu(item.PhysicalPageId,item.ChunkSlot,0,item.VirtualPageIndex)).ToArray();
        metadata = Buffer<LumonScenePatchMetadataGpu>(new LumonScenePatchMetadataGpu[captureItems.Length+1]);
        var slotData=new int[chunkCount*4]; for(int i=0;i<chunkCount;i++){slotData[i*4]=(firstChunk+i)*32;slotData[i*4+1]=32;}
        slots = Buffer<int>(slotData); readiness = Buffer<uint>(new uint[captureItems.Length+1]);
        work = Buffer<LumonSceneCaptureWorkGpu>(captureItems);
        pages.UploadDataImmediate(entries,0,0,0,128,128,chunkCount);
        Capture();
        producer = new(assets.Api);
    }

    /// <summary>Recaptures the authored faces after geometry changes, retaining the physical atlas allocation.</summary>
    public void Capture()
    {
        work.UploadSubData<LumonSceneCaptureWorkGpu>(captureItems,0,captureItems.Length*16);
        Assert.True(LumonSceneCaptureVoxelComputeShader.TryCreate(assets.Api,out var captureShader,out string log),log);
        using (captureShader)
        using (captureShader!.UseScope())
        {
            captureShader.BindSharedGeometry(Geometry.Scene); captureShader.BindCaptureWorkSsbo(work);
            captureShader.BindPatchMetaSsbo(metadata); captureShader.BindChunkSlotInfoSsbo(slots);
            captureShader.BindDepthAtlasImage(depth); captureShader.BindMaterialAtlasImage(captured);
            captureShader.SetAtlasLayout(Edge,TilesPerAxis,TilesPerAxis*TilesPerAxis,0);
            GL.DispatchCompute(1,1,captureItems.Length);
            GL.MemoryBarrier(MemoryBarrierFlags.AllBarrierBits);
        }
        using (var result=work.MapRange<LumonSceneCaptureWorkGpu>(0,captureItems.Length,MapBufferAccessMask.MapReadBit))
        { Assert.True(result.IsMapped); foreach (var item in result.Span) Assert.Equal(0u,item.VirtualPageIndex & 0x80000000u); }
    }

    /// <summary>Defines a solid boundary with explicit effective light values in all adjacent cells.</summary>
    internal TraceGeometryVoxel Sample(int x,int y,int z)
    {
        bool wall=x<=offsetX || x>=offsetX+7 || y<=32 || y>=39 || z<=0 || z>=7;
        if (DividedRoom && z==5 && (!DoorOpen || x<offsetX+2 || x>offsetX+5 || y<34 || y>37)) wall=true;
        bool exterior=x<offsetX || x>offsetX+7 || y<32 || y>39 || z<0 || z>7;
        int light=DividedRoom ? (z>=6 ? 32 : 0) : exterior ? ExteriorBlockLight ?? BlockLight : BlockLight;
        return new(wall ? 2u | materialId<<2 : 1u, LumonSceneOccupancyPacking.PackClamped(light,SunLight,0,0),0);
    }

    /// <summary>Allocates a small fixture atlas with production formats.</summary>
    private static Texture3D Texture(PixelInternalFormat format) =>
        Texture3D.Create(AtlasEdge,AtlasEdge,1,format,TextureFilterMode.Nearest,TextureTarget.Texture2DArray);

    /// <summary>Creates a typed buffer using production storage ownership.</summary>
    private static GpuShaderStorageBuffer Buffer<T>(ReadOnlySpan<T> data) where T:unmanaged
    {
        var buffer=GpuShaderStorageBuffer.Create(BufferUsageHint.DynamicDraw);
        int bytes=data.Length*Marshal.SizeOf<T>(); buffer.EnsureCapacity(bytes,growExponentially:false);
        buffer.UploadSubData(data,0,bytes); return buffer;
    }
    #endregion

    #region Lighting execution and readback
    /// <summary>Seeds direct and emitted lighting, resetting dependent indirect history.</summary>
    public void Seed()
    {
        Run(0);
        Publish();
        readiness.UploadSubData<uint>(Enumerable.Repeat(1u,captureItems.Length+1).ToArray(),0,(captureItems.Length+1)*4);
    }

    /// <summary>Runs one previous-generation bounce and publishes its complete numerical results.</summary>
    public void Bounce() { Run(1); Publish(); }

    /// <summary>Dispatches all captured pages without injecting any downstream light values.</summary>
    private void Run(uint operation)
    {
        work.UploadSubData<LumonSceneRelightWorkGpu>(lightingItems,0,lightingItems.Length*16);
        producer.Run(Geometry.Scene,Snapshot,outgoing[1-generation%2],work,lightingItems.Length,operation,64,4,256,(uint)generation,EmissionPolicy);
        using var result = work.MapRange<LumonSceneRelightWorkGpu>(0,lightingItems.Length,MapBufferAccessMask.MapReadBit);
        Assert.True(result.IsMapped);
        foreach (var item in result.Span) Assert.Equal(0u,item.VirtualPageIndex & 0x80000000u);
    }

    /// <summary>Combines into the separate generation; all fixture pages update together so no carry-forward is needed.</summary>
    private void Publish()
    {
        Run(2); generation++; GpuTestFence.WaitForGpuOrSkip("Surface outgoing generation");
        var pixels = new float[AtlasEdge * AtlasEdge * 4];
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray,0,Snapshot.OutgoingRadiance.TextureId);
        GL.GetTexImage(TextureTarget.Texture2DArray,0,PixelFormat.Rgba,PixelType.Float,pixels);
        foreach (var item in lightingItems)
        {
            int local = (int)item.PhysicalPageId - 1;
            for (int y=0; y<Edge; y++) for (int x=0; x<Edge; x++)
            {
                int offset = ((local / TilesPerAxis * Edge + y) * AtlasEdge + local % TilesPerAxis * Edge + x) * 4;
                Assert.Equal(1f,pixels[offset+3]);
                for (int c=0;c<3;c++) Assert.InRange(pixels[offset+c],0f,65504f);
            }
        }
    }

    /// <summary>Aliases a page-table entry to another captured patch without changing its physical readiness.</summary>
    public void AliasFirstPage()
    {
        var first=captureItems[0];
        uint entry=LumonScenePageTableEntryPacking.Pack(captureItems[1].PhysicalPageId,LumonScenePageTableEntryPacking.Flags.Resident).Packed;
        pages.UploadDataImmediate(new[]{entry},(int)(first.VirtualPageIndex%128),(int)(first.VirtualPageIndex/128),(int)first.ChunkSlot,1,1,1);
    }

    /// <summary>Changes the chunk generation while retaining old captured metadata, simulating slot reuse.</summary>
    public void StaleSlots()
    {
        var values=new int[chunkCount*4];
        for(int i=0;i<chunkCount;i++){values[i*4]=(firstChunk+i)*32;values[i*4+1]=32;values[i*4+3]=1;}
        slots.UploadSubData<int>(values,0,values.Length*4);
    }

    /// <summary>Withholds every cached page, exercising unavailable hit lighting without altering geometry.</summary>
    public void WithholdPages() => readiness.UploadSubData<uint>(new uint[captureItems.Length+1],0,(captureItems.Length+1)*4);

    /// <summary>Runs an expected incomplete bounce without combining or publishing its partially updated scratch history.</summary>
    public bool TryIncompleteBounce()
    {
        work.UploadSubData<LumonSceneRelightWorkGpu>(lightingItems,0,lightingItems.Length*16);
        producer.Run(Geometry.Scene,Snapshot,outgoing[1-generation%2],work,lightingItems.Length,1,64,4,256,(uint)generation,EmissionPolicy);
        using var result=work.MapRange<LumonSceneRelightWorkGpu>(0,lightingItems.Length,MapBufferAccessMask.MapReadBit);
        Assert.True(result.IsMapped);
        return result.Span.ToArray().All(item => (item.VirtualPageIndex & 0x80000000u)==0);
    }

    /// <summary>Reads a central texel of the first +X patch, avoiding non-surface corners of the voxel enclosure.</summary>
    public float[] Read(GpuTexture texture)
    {
        var pixels=new float[AtlasEdge*AtlasEdge*4];
        using var binding=GlStateCache.Current.BindTextureScope(TextureTarget.Texture2DArray,0,texture.TextureId);
        GL.GetTexImage(TextureTarget.Texture2DArray,0,PixelFormat.Rgba,PixelType.Float,pixels);
        return pixels.AsSpan((3*AtlasEdge+3)*4,4).ToArray();
    }

    /// <summary>Releases fixture resources in producer-before-input order.</summary>
    public void Dispose()
    {
        producer.Dispose(); work.Dispose();metadata.Dispose();slots.Dispose();readiness.Dispose();
        depth.Dispose();captured.Dispose();direct.Dispose();indirect.Dispose();pages.Dispose();
        foreach(var texture in outgoing) texture.Dispose();
        Geometry.Dispose(); assets.Dispose(); material.Dispose();
    }
    #endregion
}
