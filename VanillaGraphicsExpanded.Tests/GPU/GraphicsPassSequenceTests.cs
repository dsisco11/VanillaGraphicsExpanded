using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies consecutive owned pass transitions against independent pass scopes and native-state handback.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class GraphicsPassSequenceTests(HeadlessGLFixture fixture,ITestOutputHelper output):RenderTestBase(fixture)
{
    #region Public API
    #region Transition comparison
    /// <summary>Identical output and native restoration require fewer API calls when intermediate pass handbacks are omitted.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void ConsecutivePassesMatchIndependentScopesWithFewerTransitions(bool differentPrograms)
    {
        EnsureContextValid();using var scene=new SequenceScene();
        scene.Initialize();scene.DrawIndependent(differentPrograms);scene.AssertRestored();
        scene.Initialize();List<string> baseline;
        using(var capture=new GraphicsTransitionCapture()){scene.DrawIndependent(differentPrograms);baseline=[..capture.Calls];}
        scene.AssertRestored();var expected=scene.ReadOutput();
        scene.Initialize();List<string> sequence;
        using(var capture=new GraphicsTransitionCapture()){scene.DrawSequence(differentPrograms);sequence=[..capture.Calls];}
        scene.AssertRestored();var actual=scene.ReadOutput();
        for(int layer=0;layer<expected.Length;layer++)Assert.Equal(expected[layer],actual[layer]);
        Assert.Contains(actual.SelectMany(layer=>layer),value=>value==.25f);
        foreach(string prefix in new[]{"Framebuffer:","Program:","Routing"})
            Assert.True(sequence.Count(call=>call.StartsWith(prefix))<baseline.Count(call=>call.StartsWith(prefix)),
                $"{prefix} sequence={string.Join(';',sequence)} baseline={string.Join(';',baseline)}");
        Assert.True(sequence.Count<baseline.Count);
        output.WriteLine("GRAPHICS_TRANSITIONS="+System.Text.Json.JsonSerializer.Serialize(new{differentPrograms,baseline,sequence}));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Resource ownership and failure
    /// <summary>Normal EndPass releases image mutation guards but retains native framebuffer lifetime until outer restoration.</summary>
    [Fact]
    public void BetweenPassRetargetingAndResizePreserveRoutingAndOwnership()
    {
        EnsureContextValid();using var image=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var replacement=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 3), Height = System.Math.Max(1, 3), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var replacementImage=GpuFramebufferAttachment.FromTexture(replacement);
        using var target=DynamicGpuFramebuffer.CreateSingle(image)!;
        using var incoming=CreateRenderTarget(1,1,PixelInternalFormat.Rgba32f);incoming.Bind();
        Assert.True(GraphicsCommandContext.TryRun("Tests.PassSequence.Retarget",[],true,commands=>
        {
            commands.BeginPass(new(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,0,0,1))]));
            Assert.Throws<InvalidOperationException>(()=>image.Resize(3,3));commands.EndPass();
            Assert.Equal(target.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Throws<InvalidOperationException>(target.Dispose);Assert.Throws<InvalidOperationException>(()=>target.Detach());
            Assert.True(image.Resize(3,3));target.SetAttachment(FramebufferAttachment.ColorAttachment0,replacementImage);
            commands.BeginPass(new(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(.25f,.5f,.75f,1))]));
            Assert.Equal(3,commands.PassViewport.Width);commands.EndPass();
        }));
        Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
        Assert.Equal(.25f,replacement.ReadPixels()[0]);Assert.True(image.Resize(4,4));target.Dispose();Assert.True(replacement.IsValid);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Outer cleanup releases active pass images and restores native state after early return or exceptions.</summary>
    [Theory][InlineData("early")][InlineData("beforeDraw")][InlineData("afterDraw")]
    public void UnendedPassRestoresNativeStateAndReleasesBorrows(string failure)
    {
        EnsureContextValid();using var scene=new SequenceScene();scene.Initialize();
        void Run()=>scene.DrawUnended(failure);
        if(failure=="early")Run();else Assert.Throws<InvalidOperationException>(Run);
        scene.AssertRestored();scene.AssertMutable();Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Context abandonment releases both active image guards and deferred framebuffer lifetime without native restoration calls.</summary>
    [Fact]
    public void ContextAbandonmentReleasesDeferredLifetimeWithoutGlRestoration()
    {
        EnsureContextValid();using var target=CreateRenderTarget(2,2,PixelInternalFormat.Rgba32f);
        using var incoming=CreateRenderTarget(1,1,PixelInternalFormat.Rgba32f);incoming.Bind();
        Assert.True(GraphicsCommandContext.TryRun("Tests.PassSequence.Abandon",[],true,commands=>
        {
            commands.BeginPass(new(target,[new(0)]));
            var boundary=(EngineBoundaryScope)HarmonyLib.AccessTools.Field(typeof(GraphicsCommandContext),"boundary").GetValue(commands)!;
            using(var capture=new GraphicsTransitionCapture())
            {
                boundary.AbandonContext(RenderContextRegistry.Current());
                Assert.Empty(capture.Calls);
            }
        }));
        Assert.True(target[0].Resize(3,3));target.Dispose();
        incoming.Bind();StateCache.Current.InvalidateAll();Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Standalone and failed setup
    /// <summary>Standalone passes still restore framebuffer and routing immediately, without waiting for outer cleanup.</summary>
    [Fact]
    public void StandalonePassStillRestoresImmediately()
    {
        EnsureContextValid();using var scene=new SequenceScene();scene.Initialize();scene.AssertStandaloneImmediateRestore();scene.AssertRestored();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Native completeness failures after binding can be followed by a valid pass, with eventual native state restoration.</summary>
    [Fact]
    public void FailedBeginDoesNotStrandRoutingOrActiveImageBorrows()
    {
        EnsureContextValid();using var layered=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = RHITextureDimension.TwoArray, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var flat=RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var layeredImage=GpuFramebufferAttachment.FromTexture(layered);using var flatImage=GpuFramebufferAttachment.FromTexture(flat);
        using var broken=DynamicGpuFramebuffer.CreateEmpty();broken.SetAttachment(FramebufferAttachment.ColorAttachment0,layeredImage);
        broken.SetAttachment(FramebufferAttachment.ColorAttachment1,flatImage);
        using var valid=CreateRenderTarget(2,2,PixelInternalFormat.Rgba32f);using var incoming=CreateRenderTarget(1,1,PixelInternalFormat.Rgba32f);
        broken.Bind();GL.DrawBuffers(2,[DrawBuffersEnum.ColorAttachment1,DrawBuffersEnum.ColorAttachment0]); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);incoming.Bind();
        Assert.True(GraphicsCommandContext.TryRun("Tests.PassSequence.FailedBegin",[],true,commands=>
        {
            Assert.Throws<InvalidOperationException>(()=>commands.BeginPass(new(broken,[new(0),new(1)])));
            Assert.True(flat.Resize(3,3));
            commands.BeginPass(new(valid,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(.5f,0,0,1))]));commands.EndPass();
        }));
        Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
        broken.Bind();Assert.Equal((int)DrawBuffersEnum.ColorAttachment1,GL.GetInteger(GetPName.DrawBuffer0));
        Assert.Equal((int)DrawBuffersEnum.ColorAttachment0,GL.GetInteger(GetPName.DrawBuffer1));
        Assert.Equal(.5f,valid[0].ReadPixels()[0]);broken.Dispose();Assert.True(layered.IsValid);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Owns ordinary shaders and MRT destinations while retaining an independently authored hostile native baseline.</summary>
    private sealed class SequenceScene:IDisposable
    {
        private readonly List<IDisposable> owned=[];
        private readonly ComponentShaderPrograms programs;
        private readonly DebugTextureShaderProgram first,second,native;
        private readonly DynamicTexture2D source,sentinel;
        private readonly GpuSampler nativeSampler;
        private readonly GpuFramebuffer a,b,incoming,read;
        private readonly DynamicTexture2D[] outputs;
        private readonly GraphicsPipeline p,q;
        private readonly ArrayGraphicsGeometry geometry;
        private static readonly DrawBuffersEnum[] OriginalA=[DrawBuffersEnum.None,DrawBuffersEnum.ColorAttachment1,DrawBuffersEnum.ColorAttachment0];
        private static readonly DrawBuffersEnum[] OriginalB=[DrawBuffersEnum.ColorAttachment1,DrawBuffersEnum.ColorAttachment0];
        #region Public API
        #region Setup
        /// <summary>Builds the same simple textured draw for both ownership strategies without introducing a baseline shader.</summary>
        internal SequenceScene()
        {
            programs=Own(new ComponentShaderPrograms());first=programs.Create<DebugTextureShaderProgram>();second=programs.Create<DebugTextureShaderProgram>();native=programs.Create<DebugTextureShaderProgram>();
            source=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Width = 1, Height = 1, Format = RHIFormat.Rgba32f, MipLevels = 1, Storage = RHIStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { .25f,.5f,.75f,1 }).AsSpan()).ToArray())]).RequireResource());
            sentinel=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Width = 1, Height = 1, Format = RHIFormat.Rgba32f, MipLevels = 1, Storage = RHIStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 1, 1), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba32f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[] { 0f,0f,0f,1f }).AsSpan()).ToArray())]).RequireResource());nativeSampler=Own(RHICommandContext.Current.CreateSampler(new RHISamplerDescription {  }).RequireResource());
            first.Scene=source.TextureId;second.Scene=source.TextureId;native.Scene=sentinel.TextureId;
            outputs=Enumerable.Range(0,4).Select(_=>Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource())).ToArray();
            a=Own(DynamicGpuFramebuffer.CreateMRT(outputs.Take(2).ToArray())!);b=Own(DynamicGpuFramebuffer.CreateMRT(outputs.Skip(2).ToArray())!);
            incoming=CreateTarget();read=CreateTarget();
            var layout=new VertexLayoutDesc([new(0,3,VertexAttribPointerType.Float,VertexInterpretation.Floating,0,0,20),new(1,2,VertexAttribPointerType.Float,VertexInterpretation.Floating,0,12,20)]);
            var vertices=Own(RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = RHIBufferRole.Vertex, Mode = RHIStorageMode.Mutable, Capacity = 0, Usage = RHIBufferUsage.StaticDraw, Label = null }).RequireResource());vertices.UploadData(new float[]{-1,-1,0,0,0,3,-1,0,2,0,-1,3,0,0,2});
            geometry=Own(new ArrayGraphicsGeometry(layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>{{0,vertices}}));
            var lifetime=Own(new GraphicsPipelineLifetime());var signature=new RenderTargetSignature([new(PixelInternalFormat.Rgba32f)]);
            p=Own(new GraphicsPipeline(lifetime,new(first.GraphicsIdentity!,layout,signature,DynamicPipelineState.Viewport),first));
            q=Own(new GraphicsPipeline(lifetime,new(second.GraphicsIdentity!,layout,signature,DynamicPipelineState.Viewport),second));
        }
        /// <summary>Resets image contents and authors framebuffer-local routes plus distinct native read/draw/program/resource bindings.</summary>
        internal void Initialize()
        {
            foreach(var image in outputs)image.UploadDataImmediate(new float[16]);
            a.Bind();GL.DrawBuffers(OriginalA.Length,OriginalA); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);b.Bind();GL.DrawBuffers(OriginalB.Length,OriginalB); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,incoming.FboId);GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer,read.FboId);
            native.Use();GL.ActiveTexture(TextureUnit.Texture0);GL.BindTexture(TextureTarget.Texture2D,sentinel.TextureId);GL.BindSampler(0,nativeSampler.SamplerId);
            GL.ActiveTexture(TextureUnit.Texture5);StateCache.Current.InvalidateAll();
        }
        #endregion
        #region Submission
        /// <summary>Submits direct transitions and verifies that no intermediate pass returns bindings to the native owner.</summary>
        internal void DrawSequence(bool different)
        {
            var selections=new[]{p,different?q:p,p};
            Assert.True(GraphicsCommandContext.TryRun("Tests.PassSequence.Direct",[p,q],true,commands=>
            {
                for(int index=0;index<3;index++)
                {
                    var pass=Description(index);commands.BeginPass(pass);commands.SetPipeline(selections[index]);commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();
                    Assert.Equal(pass.Target.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
                    Assert.Equal(read.FboId,GL.GetInteger(GetPName.ReadFramebufferBinding));Assert.Equal(selections[index].Shader.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
                }
            }));
        }
        /// <summary>Executes independently scoped passes inside the same complete outer boundary as the comparison strategy.</summary>
        internal void DrawIndependent(bool different)
        {
            var selections=new[]{p,different?q:p,p};
            Assert.True(CompleteGraphicsBoundary.TryRun("Tests.PassSequence.Independent",[p.Description,q.Description],[first,second],true,boundary=>
            {
                for(int index=0;index<3;index++)
                {
                    using(var pass=RenderPass.Begin(Description(index),boundary))
                    {
                        var pipeline=selections[index];StateCache.Current.ApplyGraphicsState(pipeline,new(){Viewport=pass.Viewport});
                        using var shader=pipeline.Shader.UseScope(false);geometry.Submit(new(0,3));
                    }
                    Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));Assert.Equal(native.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
                }
            }));
        }
        /// <summary>Leaves one pass active to exercise outer cleanup without relying on local EndPass.</summary>
        internal void DrawUnended(string failure)
        {
            Assert.True(GraphicsCommandContext.TryRun("Tests.PassSequence.Unended",[p],true,commands=>
            {
                commands.BeginPass(Description(0));
                if(failure=="beforeDraw")throw new InvalidOperationException("Before draw");
                if(failure=="early")return;
                commands.SetPipeline(p);commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));throw new InvalidOperationException("After draw");
            }));
        }
        /// <summary>Observes restoration while a standalone pass's enclosing boundary is still alive.</summary>
        internal void AssertStandaloneImmediateRestore()
        {
            Assert.True(CompleteGraphicsBoundary.TryRun("Tests.PassSequence.Standalone",[],[],true,boundary=>
            {
                using(var pass=RenderPass.Begin(Description(0),boundary))Assert.Equal(a.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
                Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));AssertRoutes(a,OriginalA);
            }));
        }
        #endregion
        #region Inspection and disposal
        /// <summary>Checks native handback before readback can disturb the observed bindings.</summary>
        internal void AssertRestored()
        {
            Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));Assert.Equal(read.FboId,GL.GetInteger(GetPName.ReadFramebufferBinding));Assert.Equal(native.ProgramId,GL.GetInteger(GetPName.CurrentProgram));
            Assert.Equal((int)TextureUnit.Texture5,GL.GetInteger(GetPName.ActiveTexture));
            GL.ActiveTexture(TextureUnit.Texture0);Assert.Equal(sentinel.TextureId,GL.GetInteger(GetPName.TextureBinding2D));int sampler=GL.GetInteger(GetPName.SamplerBinding);Assert.Equal(nativeSampler.SamplerId,sampler);GL.ActiveTexture(TextureUnit.Texture5);
            AssertRoutes(a,OriginalA);AssertRoutes(b,OriginalB);
        }
        /// <summary>Reads only after transition counts and inherited-state assertions have completed.</summary>
        internal float[][] ReadOutput()=>outputs.Select(image=>image.ReadPixels()).ToArray();
        /// <summary>Detects leaked active image or retained framebuffer borrows after abnormal completion.</summary>
        internal void AssertMutable(){Assert.True(outputs[0].Resize(3,3));a.SetAttachment(FramebufferAttachment.ColorAttachment0,Own(GpuFramebufferAttachment.FromTexture(outputs[1])));}
        /// <summary>Releases test resources in dependency order.</summary>
        public void Dispose(){for(int index=owned.Count-1;index>=0;index--)owned[index].Dispose();}
        #endregion
        #endregion
        #region Private
        /// <summary>Uses one destination twice with a different output mapping to exercise per-framebuffer route capture.</summary>
        private RenderPassDesc Description(int index)=>new(index==1?b:a,[new(index==2?1:0)]);
        /// <summary>Tracks owned resources without changing framebuffer attachment ownership.</summary>
        private T Own<T>(T value)where T:IDisposable{owned.Add(value);return value;}
        /// <summary>Creates a native fixture target whose texture is independently owned.</summary>
        private GpuFramebuffer CreateTarget(){var image=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());return Own(DynamicGpuFramebuffer.CreateSingle(image)!);}
        /// <summary>Inspects framebuffer-local routes without changing the inherited read or draw bindings.</summary>
        private static void AssertRoutes(GpuFramebuffer target,DrawBuffersEnum[] expected)
        {
            int previous=GL.GetInteger(GetPName.DrawFramebufferBinding);GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,target.FboId);
            try{for(int index=0;index<expected.Length;index++)Assert.Equal((int)expected[index],GL.GetInteger(GetPName.DrawBuffer0+index));}
            finally{GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer,previous);StateCache.Current.InvalidateAll();}
        }
        #endregion
    }
    #endregion
}
