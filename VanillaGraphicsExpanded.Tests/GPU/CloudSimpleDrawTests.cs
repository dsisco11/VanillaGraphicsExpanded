using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes the owned Simple cloud stages with the installed normalized-short instance representation.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CloudSimpleDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Simple has the same physical response and native OIT coverage with either receiver-lighting mode.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothPbrModesUseIndependentSimpleLighting(bool lumOn)
    {
        EnsureShaderTestAvailable();
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = lumOn;
            var result = Draw(.125f, .25f, 2);
            float expected = ((10 + 33 / MathF.PI) * .75f + 2) * .125f;
            Assert.InRange(result.Radiance.X, expected * .998f, expected * 1.002f);
            Assert.InRange(result.Reveal, 0, .0001f);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }
    /// <summary>Both source directions and environment retain physical RGB; aerial loss/scatter and frame encoding enter once.</summary>
    [Theory]
    [InlineData(1f,0f,0f)]
    [InlineData(.125f,.25f,2f)]
    public void PhysicalSourceAerialAndOitAreAppliedOnce(float scale, float loss, float scatter)
    {
        EnsureShaderTestAvailable();
        var result=Draw(scale,loss,scatter);
        float expected=(10+(30+3)/MathF.PI)*(1-loss)+scatter;
        for(int channel=0;channel<3;channel++)
            Assert.InRange(result.Radiance[channel],expected*scale*.998f,expected*scale*1.002f);
        Assert.InRange(result.Reveal,0,.0001f);
        Assert.InRange(result.Glow[0],.249f,.251f);
        Assert.Equal(0,result.Glow[1]); Assert.Equal(0,result.Glow[2]);
        double fauxDepth=(9.81981982*9.81981982*9.81981982*(1-9.81981982/1000)/250000)/30;
        for(int bin=0;bin<3;bin++)
        {
            double bell=Math.Exp(-Math.Pow((Math.Log(1+fauxDepth)-bin)/.832,2));
            Assert.InRange(Math.Abs(result.BucketReveal[bin]-(1-bell)),0,.00002);
            Assert.InRange(Math.Abs(result.BucketAlpha[bin]-bell*Math.Exp(-fauxDepth/100)),0,.00002);
        }
    }

    /// <summary>Shared occupied sides disappear while exposed sides and bottom/top native alpha controls remain distinct.</summary>
    [Theory]
    [InlineData(0,.1f,false)]
    [InlineData(0,0f,true)]
    [InlineData(4,.1f,true)]
    [InlineData(5,.1f,true)]
    public void NeighborAndFaceCoverageUsesNativeShorts(int face,float neighbor,bool visible)
    {
        EnsureShaderTestAvailable(); var result=Draw(1,0,0,face,neighbor);
        if(!visible) { Assert.Equal(1,result.Reveal); Assert.Equal(Vector3.Zero,result.Radiance); }
        else if(face==4) Assert.InRange(result.Reveal,.099f,.101f);
        else Assert.InRange(result.Reveal,0,.0001f);
    }

    /// <summary>Local intensity moves geometry without mutating global uniforms, and normalized offsets decode back to world units.</summary>
    [Fact]
    public void InstancePlacementAndPerceptionChangeOnlyTheOwnedDraw()
    {
        EnsureShaderTestAvailable();
        var ordinary=Draw(1,0,0);
        var shifted=Draw(1,0,0,tileX:20);
        Assert.True(ordinary.VisiblePixels>0); Assert.Equal(0,shifted.VisiblePixels);
        var warped=Draw(1,0,0,perception:4);
        Assert.NotEqual(ordinary.RevealPixels,warped.RevealPixels);
        var bright=Draw(1,0,0,brightness:0);
        Assert.Equal(ordinary.Radiance,bright.Radiance);
    }

    /// <summary>A foreground opaque depth rejects clouds while liquid depth invokes their underwater color branch without clipping coverage.</summary>
    [Fact]
    public void OpaqueAndLiquidDepthHaveDistinctEffects()
    {
        EnsureShaderTestAvailable();
        Assert.Equal(0,Draw(1,0,0,opaqueDepth:0).VisiblePixels);
        var dry=Draw(1,0,0); var wet=Draw(1,0,0,liquidDepth:0);
        Assert.Equal(dry.Reveal,wet.Reveal); Assert.True(wet.Radiance.X<dry.Radiance.X*.1f);
    }

    /// <summary>Own thickness expands upper vertices, thin mode removes that expansion, and undulation remains mesh-only animation.</summary>
    [Fact]
    public void HeightThinnessAndUndulationRetainGeometryControls()
    {
        EnsureShaderTestAvailable();
        var ordinary=Draw(1,0,0);
        var thick=Draw(1,0,0,thickness:.4f);
        var thin=Draw(1,0,0,thickness:.4f,thin:1);
        Assert.NotEqual(ordinary.RevealPixels,thick.RevealPixels);
        Assert.Equal(ordinary.RevealPixels,thin.RevealPixels);
        Assert.NotEqual(ordinary.RevealPixels,Draw(1,0,0,undulation:1).RevealPixels);
    }

    /// <summary>The geometry adapter rejects unsupported native streams and excess instance ranges without deleting the mesh.</summary>
    [Fact]
    public void InvalidNativeRepresentationCannotBeAdmitted()
    {
        EnsureShaderTestAvailable();
        using var native=new NativeCloud(5,.1f,0,short.MaxValue,.1f,0,0);
        using(var borrowed=new CloudSimpleGeometry(native.Mesh,1)) { }
        Assert.Throws<ArgumentOutOfRangeException>(()=>new CloudSimpleGeometry(native.Mesh,0));
        Assert.Throws<InvalidOperationException>(()=>new CloudSimpleGeometry(native.Mesh,2));
        // A new managed identity has no previous receipt, so a malformed divisor is checked at acquisition.
        using var malformed=new NativeCloud(5,.1f,0,short.MaxValue,.1f,0,0);
        StateCache.Current.BindVertexArray(malformed.Mesh.VaoId); GL.VertexAttribDivisor(4,0);
        Assert.Throws<NotSupportedException>(()=>new CloudSimpleGeometry(malformed.Mesh,1));
        using var packed=new NativeCloud(5,.1f,0,short.MaxValue,.1f,0,0);
        StateCache.Current.BindVertexArray(packed.Mesh.VaoId);
        StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer,packed.Mesh.customDataShortVboId);
        GL.VertexAttribPointer(3,2,VertexAttribPointerType.Short,true,0,IntPtr.Zero);
        Assert.Throws<NotSupportedException>(()=>new CloudSimpleGeometry(packed.Mesh,1));
        Assert.False(native.Mesh.Disposed); Assert.True(GL.IsBuffer(native.Mesh.customDataShortVboId));
    }
    #endregion

    #region Private
    /// <summary>Keeps resolved straight source measurements separate from dimensionless OIT outputs.</summary>
    private sealed record Result(Vector3 Radiance,float Reveal,float[] Glow,int VisiblePixels,float[] RevealPixels,
        Vector3 BucketReveal,Vector3 BucketAlpha);

    /// <summary>Runs the real binary executable, native-layout geometry and six-attachment blending in a small controlled view.</summary>
    private Result Draw(float scale,float loss,float scatter,int face=5,float neighbor=.1f,short tileX=0,float perception=0,
        short brightness=short.MaxValue,float opaqueDepth=1,float liquidDepth=1,float thickness=.1f,float thin=0,float undulation=0)
    {
        const int size=16;
        float[] projection=[1,0,0,0,0,1,0,0,0,0,-1.002002f,-1,0,0,-.2002002f,0];
        float[] identity=[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1];
        using var camera=TestFrameCamera.CreateFromProjection(projection,size,size,.1f,100);
        using var storage=new TestFrameLightingPublication(); storage.Capture(scale);
        var program=Programs.Create<CloudSimpleProgram>(); program.Frame=camera; program.Lighting=storage.Buffer;
        using var native=new NativeCloud(face,neighbor,tileX,brightness,thickness,thin,undulation);
        using var geometry=new CloudSimpleGeometry(native.Mesh,1);
        var lighting=new AtmosphereLighting(Vector3.UnitZ,new(30),new(10),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
        { Lunar=new(3),Moon=-Vector3.UnitZ,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale,
            ReconstructionMaximumNits=scatter, HorizonElevation=-.1f };
        program.Parameters.CaptureGeometry(projection,identity,new(0,0,0,1000),new(0,0,0,1),new(2,perception,50,0),Vector4.Zero);
        program.Parameters.CaptureLighting(lighting,0,Vector4.Zero,Vector3.Zero,Vector3.Zero);
        using var aerial=DynamicTexture3D.Create(1,2,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D);
        using var attenuation=DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D);
        using var liquid=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        using var shafts=DynamicTexture2D.Create(1,1,PixelInternalFormat.R32f);
        aerial.UploadDataImmediate([scatter/16,scatter/16,scatter/16,0,0,0,0,0],0,0,0,1,2,1,0);
        attenuation.UploadDataImmediate([loss,loss,loss,0],0,0,0,1,1,1,0);
        liquid.UploadDataImmediate(new float[]{liquidDepth}); shafts.UploadDataImmediate(new float[]{0});
        program.AerialRadiance=aerial; program.AerialAttenuation=attenuation; program.LiquidDepth=liquid; program.LightShafts=shafts;
        using var reveal=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgb32f);
        using var overall=DynamicTexture2D.Create(size,size,PixelInternalFormat.R32f);
        using var glow=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var a0=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var a1=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var a2=DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var depth=new DepthTexture(size,size,PixelInternalFormat.DepthComponent32f);
        using var target=DynamicGpuFramebuffer.CreateMRT([reveal,overall,glow,a0,a1,a2],depth)!;
        var pass=new RenderPassDesc(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
            new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
            new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(4,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(5,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))],new(DepthLoad:AttachmentLoad.Clear,ClearDepth:opaqueDepth));
        using var metadata=new RenderPassTargets(pass); using var lifetime=new GraphicsPipelineLifetime();
        using var pipeline=new GraphicsPipeline(lifetime,new(program.GraphicsIdentity!,CloudSimpleGeometry.Layout,metadata.Signature,
            DynamicPipelineState.Viewport,depthStencil:LiquidPipelineStates.Surface,blending:LiquidPipelineStates.SurfaceBlending),program);
        int priorVao=GL.GetInteger(GetPName.VertexArrayBinding); bool priorDepth=GL.IsEnabled(EnableCap.DepthTest);
        Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Simple",[pipeline],true,commands=>
        {
            commands.BeginPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new(){Viewport=commands.PassViewport});
            commands.Draw(geometry,new(0,36)); commands.EndPass();
        }));
        Assert.Equal(priorVao,GL.GetInteger(GetPName.VertexArrayBinding)); Assert.Equal(priorDepth,GL.IsEnabled(EnableCap.DepthTest));
        Assert.False(native.Mesh.Disposed); Assert.True(GL.IsBuffer(native.Mesh.customDataShortVboId));
        float[] accumulation=a0.ReadPixels(), reveals=overall.ReadPixels(), glows=glow.ReadPixels();
        foreach(var image in new[]{accumulation,reveals,glows,a1.ReadPixels(),a2.ReadPixels(),reveal.ReadPixels()})
            Assert.All(image,value=>Assert.True(float.IsFinite(value)&&value>=0));
        Assert.All(reveals,value=>Assert.InRange(value,0,1));
        int pixel=size/2*size+size/2, offset=pixel*4; float alpha=accumulation[offset+3];
        Vector3 radiance=alpha>0?new Vector3(accumulation[offset],accumulation[offset+1],accumulation[offset+2])/alpha:Vector3.Zero;
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        float[] bucketReveal=reveal.ReadPixels();
        return new(radiance,reveals[pixel],glows[offset..(offset+4)],reveals.Count(value=>value<.999f),reveals,
            new(bucketReveal[pixel*3],bucketReveal[pixel*3+1],bucketReveal[pixel*3+2]),
            new(alpha,a1.ReadPixels()[offset+3],a2.ReadPixels()[offset+3]));
    }

    /// <summary>Owns fixture storage matching the installed 24-vertex template and normalized 12-short instance record.</summary>
    private sealed class NativeCloud : IDisposable
    {
        internal readonly VAO Mesh;
        private readonly GpuVao vao=GpuVao.Create();
        private readonly GpuEbo indices=GpuEbo.Create();
        private readonly GpuVbo[] streams=Enumerable.Range(0,4).Select(_=>GpuVbo.Create()).ToArray();
        #region Public API
        /// <summary>Authors the native representation independently of the production adapter's layout descriptor.</summary>
        internal NativeCloud(int face,float neighbor,short tileX,short brightness,float thickness,float thin,float undulation)
        {
            vao.Bind();
            float[] positions=new float[72]; new float[]{-4,-4,-10,4,-4,-10,4,4,-10,-4,4,-10}.CopyTo(positions,0);
            streams[0].UploadData(positions); streams[1].UploadData(Enumerable.Repeat((byte)255,96).ToArray());
            streams[2].UploadData(Enumerable.Repeat((uint)face,24).ToArray());
            short n=(short)MathF.Round(neighbor*32767);
            streams[3].UploadData(new short[]{tileX,0,n,n,n,n,(short)MathF.Round(thickness*32767),brightness,
                (short)MathF.Round(thin*32767),(short)MathF.Round(undulation*32767),short.MaxValue,0});
            uint[] elements=new uint[36]; new uint[]{0,1,2,0,2,3}.CopyTo(elements,0); indices.UploadIndices(elements); vao.BindElementBuffer(indices);
            int[] components=[3,4,1,2,4,1,1,1,1,1]; int[] offsets=[0,0,0,0,4,12,14,16,18,20];
            for(int i=0;i<10;i++)
            {
                int binding=i<3?i:3; StateCache.Current.BindBuffer(BufferTarget.ArrayBuffer,streams[binding].BufferId);
                if(i==2) GL.VertexAttribIPointer(i,1,VertexAttribIntegerType.UnsignedInt,4,IntPtr.Zero);
                else GL.VertexAttribPointer(i,components[i],i==0?VertexAttribPointerType.Float:i==1?VertexAttribPointerType.UnsignedByte:VertexAttribPointerType.Short,
                    i!=0,i==0?12:i==1?4:24,(IntPtr)offsets[i]);
                GL.EnableVertexAttribArray(i); GL.VertexAttribDivisor(i,i<3?0:1);
            }
            Mesh=(VAO)RuntimeHelpers.GetUninitializedObject(typeof(VAO)); GC.SuppressFinalize(Mesh);
            Mesh.VaoId=vao.VertexArrayId; Mesh.vboIdIndex=indices.BufferId; Mesh.IndicesCount=36; Mesh.drawMode=PrimitiveType.Triangles;
            Mesh.xyzVboId=streams[0].BufferId; Mesh.rgbaVboId=streams[1].BufferId; Mesh.flagsVboId=streams[2].BufferId; Mesh.customDataShortVboId=streams[3].BufferId;
        }
        /// <summary>Deletes only test-owned allocations after borrowed production geometry has expired.</summary>
        public void Dispose() { vao.Dispose(); indices.Dispose(); foreach(var stream in streams) stream.Dispose(); }
        #endregion
    }
    #endregion
}
