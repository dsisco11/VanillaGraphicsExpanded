using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
[Trait("Category", "GpuIntegration")]
public sealed class CloudSimpleDrawTests(HeadlessGLFixture fixture) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Recorded submission consumes native cloud streams only inside their live synchronous borrowing interval.</summary>
    [Fact]
    public void RecordedCloudMatchesImmediatePixelsAndPreservesBorrowedStorage()
    {
        EnsureShaderTestAvailable();
        using var immediate = Draw(.125f, .25f, 2);
        using var recorded = Draw(.125f, .25f, 2, record: true);
        Assert.Equal(immediate.Radiance, recorded.Radiance);
        ImageAssertions.Comparisons.CompareHistory(recorded.RevealPixels.Texture, immediate.RevealPixels).AssertEqual();
        Assert.Equal(immediate.BucketAlpha, recorded.BucketAlpha);
    }

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
            using var result = Draw(.125f, .25f, 2);
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
        using var result = Draw(scale,loss,scatter);
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
        EnsureShaderTestAvailable(); using var result = Draw(1,0,0,face,neighbor);
        if(!visible) { Assert.Equal(1,result.Reveal); Assert.Equal(Vector3.Zero,result.Radiance); }
        else if(face==4) Assert.InRange(result.Reveal,.099f,.101f);
        else Assert.InRange(result.Reveal,0,.0001f);
    }

    /// <summary>Local intensity moves geometry without mutating global uniforms, and normalized offsets decode back to world units.</summary>
    [Fact]
    public void InstancePlacementAndPerceptionChangeOnlyTheOwnedDraw()
    {
        EnsureShaderTestAvailable();
        using var ordinary = Draw(1,0,0);
        using var shifted = Draw(1,0,0,tileX:20);
        Assert.True(ordinary.VisiblePixels>0); Assert.Equal(0,shifted.VisiblePixels);
        using var warped = Draw(1,0,0,perception:4);
        Assert.True(ImageAssertions.Comparisons.CompareHistory(warped.RevealPixels.Texture, ordinary.RevealPixels).Mismatches > 0);
        using var bright = Draw(1,0,0,brightness:0);
        Assert.Equal(ordinary.Radiance,bright.Radiance);
    }

    /// <summary>A foreground opaque depth rejects clouds while liquid depth invokes their underwater color branch without clipping coverage.</summary>
    [Fact]
    public void OpaqueAndLiquidDepthHaveDistinctEffects()
    {
        EnsureShaderTestAvailable();
        using var occluded = Draw(1,0,0,opaqueDepth:0);
        Assert.Equal(0,occluded.VisiblePixels);
        using var dry = Draw(1,0,0); using var wet = Draw(1,0,0,liquidDepth:0);
        Assert.Equal(dry.Reveal,wet.Reveal); Assert.True(wet.Radiance.X<dry.Radiance.X*.1f);
    }

    /// <summary>Own thickness expands upper vertices, thin mode removes that expansion, and undulation remains mesh-only animation.</summary>
    [Fact]
    public void HeightThinnessAndUndulationRetainGeometryControls()
    {
        EnsureShaderTestAvailable();
        using var ordinary = Draw(1,0,0);
        using var thick = Draw(1,0,0,thickness:.4f);
        using var thin = Draw(1,0,0,thickness:.4f,thin:1);
        Assert.True(ImageAssertions.Comparisons.CompareHistory(thick.RevealPixels.Texture, ordinary.RevealPixels).Mismatches > 0);
        ImageAssertions.Comparisons.CompareHistory(thin.RevealPixels.Texture, ordinary.RevealPixels).AssertEqual();
        using var animated = Draw(1,0,0,undulation:1);
        Assert.True(ImageAssertions.Comparisons.CompareHistory(animated.RevealPixels.Texture, ordinary.RevealPixels).Mismatches > 0);
    }

    /// <summary>Observed native edits expire existing declarations without transferring engine mesh ownership.</summary>
    [Fact]
    public void InvalidNativeRepresentationCannotBeAdmitted()
    {
        EnsureShaderTestAvailable();
        using var native=new NativeCloud(5,.1f,0,short.MaxValue,.1f,0,0);
        using(var borrowed=new CloudSimpleGeometry(native.Mesh,1)) { }
        Assert.Throws<ArgumentOutOfRangeException>(()=>new CloudSimpleGeometry(native.Mesh,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>new CloudSimpleGeometry(native.Mesh,2));
        // Admission uses the declared layout; subsequent intercepted edits expire that exact configuration.
        using var malformed=new NativeCloud(5,.1f,0,short.MaxValue,.1f,0,0);
        using var previousDivisor = new CloudSimpleGeometry(malformed.Mesh,1);
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, malformed.Mesh.VaoId); GL.VertexAttribDivisor(4,0);
        Assert.Throws<InvalidOperationException>(()=>previousDivisor.Inputs);
        using var declaredDivisor = new CloudSimpleGeometry(malformed.Mesh,1);
        Assert.NotEmpty(declaredDivisor.Inputs.Streams);
        using var packed=new NativeCloud(5,.1f,0,short.MaxValue,.1f,0,0);
        using var previousPacking = new CloudSimpleGeometry(packed.Mesh,1);
        OpenGLVertexSelection.Bind(OpenGlStateCache.Current, packed.Mesh.VaoId);
        OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer,packed.Mesh.customDataShortVboId);
        GL.VertexAttribPointer(3,2,VertexAttribPointerType.Short,true,0,IntPtr.Zero);
        Assert.Throws<InvalidOperationException>(()=>previousPacking.Inputs);
        using var declaredPacking = new CloudSimpleGeometry(packed.Mesh,1);
        Assert.NotEmpty(declaredPacking.Inputs.Streams);
        Assert.False(native.Mesh.Disposed); Assert.True(GL.IsBuffer(native.Mesh.customDataShortVboId));
    }
    #endregion

    #region Private
    /// <summary>Keeps resolved straight source measurements separate from dimensionless OIT outputs.</summary>
    private sealed record Result(Vector3 Radiance, float Reveal, float[] Glow, int VisiblePixels,
        Assertions.GpuImageSnapshot RevealPixels, Vector3 BucketReveal, Vector3 BucketAlpha) : IDisposable
    {
        #region Public API
        /// <summary>Releases the independent coverage image retained for cross-draw assertions.</summary>
        public void Dispose() => RevealPixels.Dispose();
        #endregion
    }

    /// <summary>Runs the real binary executable, native-layout geometry and six-attachment blending in a small controlled view.</summary>
    private Result Draw(float scale,float loss,float scatter,int face=5,float neighbor=.1f,short tileX=0,float perception=0,
        short brightness=short.MaxValue,float opaqueDepth=1,float liquidDepth=1,float thickness=.1f,float thin=0,float undulation=0,bool record=false)
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
        using var aerial=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var attenuation=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var liquid=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var shafts=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        aerial.UploadDataImmediate([scatter/16,scatter/16,scatter/16,0,0,0,0,0],0,0,0,1,2,1,0);
        attenuation.UploadDataImmediate([loss,loss,loss,0],0,0,0,1,1,1,0);
        liquid.UploadDataImmediate(new float[]{liquidDepth}); shafts.UploadDataImmediate(new float[]{0});
        program.AerialRadiance=aerial; program.AerialAttenuation=attenuation; program.LiquidDepth=liquid; program.LightShafts=shafts;
        using var reveal=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgb32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var overall=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var glow=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var a0=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var a1=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var a2=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var depth=new DepthTexture(size,size,TextureFormat.DepthComponent32f);
        using var target=DynamicGpuFramebuffer.CreateMRT([reveal,overall,glow,a0,a1,a2],depth)!;
        var pass=FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
            new(AttachmentIndex:1, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
            new(AttachmentIndex:2, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(AttachmentIndex:3, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(AttachmentIndex:4, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(AttachmentIndex:5, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))], new(DepthLoad:AttachmentLoad.Clear,ClearDepth:opaqueDepth));
        var metadata = RHICommandContext.Current.DescribeRenderTargets(pass); 
        using var pipeline=RHICommandContext.Current.CreateGraphicsPipeline(new(program.Shader,CloudSimpleGeometry.Layout,metadata,
            DynamicPipelineState.Viewport,depthStencil:LiquidPipelineStates.Surface,blending:LiquidPipelineStates.SurfaceBlending)).RequireResource();
        int priorVao=GL.GetInteger(GetPName.VertexArrayBinding); bool priorDepth=GL.IsEnabled(EnableCap.DepthTest);
        {
    var commands=RHICommandContext.Current;
    using var scope24=commands.EnterBoundary(new("Tests.Cloud.Simple",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope24.Run(()=>{
            if (record)
            {
                using var list = commands.CreateCommandList();
                list.BeginRenderPass(pass); list.SetPipeline(pipeline);
                list.SetDynamicState(new() { Viewport = new(0, 0, size, size) });
                list.BindResources(program.CaptureBindings()); list.DrawIndexed(geometry.Inputs, new(0, 36)); list.EndRenderPass();
                list.Close(conditionalRenderingInactive: true);
                var result = commands.Submit(list);
                Assert.Null(result.Error); Assert.Equal(CommandListState.Executed, result.State);
            }
            else
            {
                commands.BeginRenderPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new(){Viewport=commands.PassViewport});
                commands.BindResources(program.CaptureBindings()); commands.DrawIndexed(geometry.Inputs,new(0,36)); commands.EndRenderPass();
            }
        });
}
        Assert.Equal(priorVao,GL.GetInteger(GetPName.VertexArrayBinding)); Assert.Equal(priorDepth,GL.IsEnabled(EnableCap.DepthTest));
        Assert.False(native.Mesh.Disposed); Assert.True(GL.IsBuffer(native.Mesh.customDataShortVboId));
        foreach (var image in new[] {a0, overall, glow, a1, a2, reveal})
            ImageAssertions.Statistics.Inspect(image, 0).AssertFiniteRange();
        ImageAssertions.Statistics.Inspect(overall, 0, 1).AssertFiniteRange();
        float[] accumulation = a0.ReadPixelsRegion(size/2, size/2, 1, 1);
        float[] glows = glow.ReadPixelsRegion(size/2, size/2, 1, 1);
        float alpha = accumulation[3];
        Vector3 radiance = alpha > 0 ? new Vector3(accumulation[0],accumulation[1],accumulation[2])/alpha : Vector3.Zero;
        float[] bucketReveal = reveal.ReadPixelsRegion(size/2, size/2, 1, 1);
        int visible = (int)ImageAssertions.Statistics.Inspect(overall, double.NegativeInfinity, MathF.BitDecrement(.999f)).InsideRange;
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        return new(radiance, overall.ReadPixelsRegion(size/2,size/2,1,1)[0], glows, visible, new(overall),
            new(bucketReveal[0],bucketReveal[1],bucketReveal[2]),
            new(alpha,a1.ReadPixelsRegion(size/2,size/2,1,1)[3],a2.ReadPixelsRegion(size/2,size/2,1,1)[3]));
    }

    /// <summary>Owns fixture storage matching the installed 24-vertex template and normalized 12-short instance record.</summary>
    private sealed class NativeCloud : IDisposable
    {
        internal readonly VAO Mesh;
        private readonly GpuVao vao=NativeResourceSetup.CreateVertexArray();
        private readonly GpuEbo indices=RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription { Role = BufferRole.Index, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
        private readonly GpuVbo[] streams=Enumerable.Range(0,4).Select(_=>RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource()).ToArray();
        #region Public API
        /// <summary>Authors the native representation independently of the production adapter's layout descriptor.</summary>
        internal NativeCloud(int face,float neighbor,short tileX,short brightness,float thickness,float thin,float undulation)
        {
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            float[] positions=new float[72]; new float[]{-4,-4,-10,4,-4,-10,4,4,-10,-4,4,-10}.CopyTo(positions,0);
            streams[0].UploadData(positions); streams[1].UploadData(Enumerable.Repeat((byte)255,96).ToArray());
            streams[2].UploadData(Enumerable.Repeat((uint)face,24).ToArray());
            short n=(short)MathF.Round(neighbor*32767);
            streams[3].UploadData(new short[]{tileX,0,n,n,n,n,(short)MathF.Round(thickness*32767),brightness,
                (short)MathF.Round(thin*32767),(short)MathF.Round(undulation*32767),short.MaxValue,0});
            uint[] elements=new uint[36]; new uint[]{0,1,2,0,2,3}.CopyTo(elements,0); indices.UploadIndices(elements); OpenTK.Graphics.OpenGL4.GL.VertexArrayElementBuffer(vao.VertexArrayId, indices.BufferId);
            int[] components=[3,4,1,2,4,1,1,1,1,1]; int[] offsets=[0,0,0,0,4,12,14,16,18,20];
            for(int i=0;i<10;i++)
            {
                int binding=i<3?i:3; OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer,streams[binding].BufferId);
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
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
    }
    #endregion
}
