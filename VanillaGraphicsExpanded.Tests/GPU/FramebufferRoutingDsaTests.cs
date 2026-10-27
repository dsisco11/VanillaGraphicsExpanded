using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies core named framebuffer routing, repeated restoration and independent cleanup.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class FramebufferRoutingDsaTests(HeadlessGLFixture fixture,ITestOutputHelper output):RenderTestBase(fixture)
{
    #region Public API
    #region Equivalent restoration
    /// <summary>Repeated named restoration preserves images and routes in standalone and shared boundaries.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void NamedRestorationProducesEquivalentResults(bool standalone)
    {
        EnsureContextValid();Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        // Clear-only passes declare no executable; preserve any program left by another shared-fixture test.
        OpenGLProgramSelection.Select(StateCache.Current, 0);
        using var scene=new RoutingScene();scene.Initialize();List<string> fallback;
        using(var capture=new GraphicsTransitionCapture()){scene.Render(standalone);fallback=[..capture.Calls];}
        scene.AssertRestored();var expected=scene.ReadOutput();scene.Initialize();List<string> named;
        using(var capture=new GraphicsTransitionCapture()){scene.Render(standalone);named=[..capture.Calls];}
        scene.AssertRestored();var actual=scene.ReadOutput();
        for(int layer=0;layer<expected.Length;layer++)Assert.Equal(expected[layer],actual[layer]);
        Assert.Equal(.25f,actual[0][0]);Assert.Equal(.5f,actual[3][0]);
        Assert.Equal(4,fallback.Count(call=>call.StartsWith("RoutingNamed:")));
        Assert.Equal(4,named.Count(call=>call.StartsWith("RoutingNamed:")));
        Assert.Equal(fallback.Count(call=>call.StartsWith("Routing")),named.Count(call=>call.StartsWith("Routing")));
        Assert.Equal(fallback.Count(call=>call.StartsWith("Framebuffer:")),named.Count(call=>call.StartsWith("Framebuffer:")));
        output.WriteLine("FRAMEBUFFER_ROUTING_DSA="+System.Text.Json.JsonSerializer.Serialize(new{standalone,fallback,named}));
        scene.AssertReleased();Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Grouped default-window selectors restore with DrawBuffer even on contexts that support direct state access.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void WindowRestorationAlwaysPreservesGroupedSelector(bool directStateAccess)
    {
        EnsureContextValid();Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        // Clear-only passes declare no executable; preserve any program left by another shared-fixture test.
        OpenGLProgramSelection.Select(StateCache.Current,0);
        using var capability=new DirectStateAccessCapabilityScope(directStateAccess);
        using var originalBindings=StateCache.Current.BindFramebufferScope();
        using var window=DynamicGpuFramebuffer.Wrap(0,width:1,height:1);window.PublishSurfaceMetadata(1,1);
        var metadata=window.Surface!;
        DrawBufferMode grouped=metadata.Buffer==DrawBuffersEnum.BackLeft?DrawBufferMode.Back:DrawBufferMode.Front;
        OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer,0);int previous=GL.GetInteger(GetPName.DrawBuffer0);GL.DrawBuffer(grouped); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);
        OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.ReadFramebuffer,0);int previousRead=GL.GetInteger(GetPName.ReadBuffer);
        using var incoming=CreateRenderTarget(1,1,PixelInternalFormat.Rgba32f);incoming.Bind();
        try
        {
            using(var capture=new GraphicsTransitionCapture())
            {
                {
    var commands=RHICommandContext.Current;
    using var scope44=commands.EnterBoundary(new("Tests.Dsa.Window",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope44.Run(()=>{
                    commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(window, [new(Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(.25f,.5f,.75f,1),WindowBuffer:metadata.Buffer==DrawBuffersEnum.BackLeft?SurfaceColorBuffer.BackLeft:SurfaceColorBuffer.FrontLeft)]));commands.EndRenderPass();
                });
}
                Assert.Contains(capture.Calls,call=>call==$"RoutingWindow:0:{grouped}");
                Assert.DoesNotContain(capture.Calls,call=>call.StartsWith("RoutingNamed:"));
                output.WriteLine("WINDOW_ROUTING_DSA="+System.Text.Json.JsonSerializer.Serialize(new{directStateAccess,grouped=grouped.ToString(),calls=capture.Calls}));
            }
            Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
            OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer,0);Assert.Equal((int)grouped,GL.GetInteger(GetPName.DrawBuffer0));
            OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.ReadFramebuffer,0);GL.ReadBuffer((ReadBufferMode)metadata.Buffer);
            using var pack=OpenGLPixelLayouts.PackScope(StateCache.Current, new(1));byte[] pixel=new byte[4];GL.ReadPixels(0,0,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,pixel);
            Assert.InRange(pixel[0],(byte)63,(byte)65);
        }
        finally{OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer,0);GL.DrawBuffer((DrawBufferMode)previous); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.ReadFramebuffer,0);GL.ReadBuffer((ReadBufferMode)previousRead);StateCache.Current.InvalidateAll();}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Independent cleanup
    /// <summary>A failed named restore cannot prevent another target's routing or the enclosing native bindings from being restored.</summary>
    [Fact]
    public void NamedRestoreFailureStillCleansRemainingOwners()
    {
        EnsureContextValid();Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        // Clear-only passes declare no executable; preserve any program left by another shared-fixture test.
        OpenGLProgramSelection.Select(StateCache.Current,0);
        using var scene=new RoutingScene();scene.Initialize();

        using(var capture=new GraphicsTransitionCapture()){capture.FailNamedFramebuffer=scene.FirstFramebuffer;var failure=Assert.ThrowsAny<Exception>(()=>scene.Render(false));Assert.Contains("Injected named restoration failure",failure.ToString());}
        scene.AssertRestored(includeFirst:false);scene.AssertReleased();Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Owns two two-image targets and separate incoming read/draw bindings for routing-only comparisons.</summary>
    private sealed class RoutingScene:IDisposable
    {
        private readonly List<IDisposable> owned=[];
        private readonly DynamicTexture2D[] images;
        private readonly GpuFramebuffer[] targets;
        private readonly GpuFramebuffer incoming,read;
        private static readonly DrawBuffersEnum[][] Original=[[DrawBuffersEnum.None,DrawBuffersEnum.ColorAttachment1,DrawBuffersEnum.ColorAttachment0],[DrawBuffersEnum.ColorAttachment1,DrawBuffersEnum.ColorAttachment0]];
        #region Public API
        #region Setup and submission
        /// <summary>Creates independently owned images and framebuffers without any shader or legacy pipeline fixture.</summary>
        internal RoutingScene()
        {
            images=Enumerable.Range(0,4).Select(_=>Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource())).ToArray();
            targets=[Own(DynamicGpuFramebuffer.CreateMRT(images.Take(2).ToArray())!),Own(DynamicGpuFramebuffer.CreateMRT(images.Skip(2).ToArray())!)];
            // Model real external framebuffer surfaces so handback concerns the original engine IDs,
            // rather than the backend's independently realized attachment-based pass targets.
            targets=targets.Select(target=>
            {
                var external=Own(DynamicGpuFramebuffer.Wrap(target.FboId));
                external.RefreshWrappedFramebuffer(target.FboId,2,2,publishPassMetadata:true);
                return (GpuFramebuffer)external;
            }).ToArray();
            incoming=CreateTarget();read=CreateTarget();
        }
        /// <summary>Identifies the first cleanup owner for controlled failure injection.</summary>
        internal int FirstFramebuffer=>targets[0].FboId;
        /// <summary>Seeds exact route history and neutral image contents before either compared execution.</summary>
        internal void Initialize()
        {
            foreach(var image in images)image.UploadDataImmediate(new float[16]);
            for(int index=0;index<targets.Length;index++){targets[index].Bind();GL.DrawBuffers(Original[index].Length,Original[index]); StateCache.Current.Invalidate(EPipelineState.FramebufferRouting);}
            OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer,incoming.FboId);OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.ReadFramebuffer,read.FboId);
        }
        /// <summary>Runs two equivalent clears using either independent passes or one deferred-restoration sequence.</summary>
        internal void Render(bool standalone)
        {
            if(standalone)
            {
                var commands=RHICommandContext.Current;
                for(int index=0;index<2;index++)
                {
                    using var boundary=commands.EnterBoundary(new("Tests.Dsa.Standalone",true,operations:BoundaryOperations.RenderPasses));
                    int selection=index;
                    boundary.Run(()=>{commands.BeginRenderPass(Description(selection));commands.EndRenderPass();});
                }
            }
            else {
    var commands=RHICommandContext.Current;
    using var scope45=commands.EnterBoundary(new("Tests.Dsa.Sequence",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope45.Run(()=>{
                for(int index=0;index<2;index++){commands.BeginRenderPass(Description(index));commands.EndRenderPass();}
            });
}
        }
        #endregion
        #region Inspection and disposal
        /// <summary>Checks native handback and independently inspects every successfully restored framebuffer route.</summary>
        internal void AssertRestored(bool includeFirst=true)
        {
            Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));Assert.Equal(read.FboId,GL.GetInteger(GetPName.ReadFramebufferBinding));
            for(int index=includeFirst?0:1;index<2;index++)
            {
                OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer,targets[index].FboId);
                for(int slot=0;slot<Original[index].Length;slot++)Assert.Equal((int)Original[index][slot],GL.GetInteger(GetPName.DrawBuffer0+slot));
            }
            OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer,incoming.FboId);
        }
        /// <summary>Reads physical outputs after capture without changing the compared operation count.</summary>
        internal float[][] ReadOutput()=>images.Select(image=>image.ReadPixels()).ToArray();
        /// <summary>Requires every deferred framebuffer lifetime and active image borrow to have ended.</summary>
        internal void AssertReleased(){foreach(var image in images)Assert.True(image.Resize(3,3));foreach(var target in targets)target.Dispose();}
        /// <summary>Releases only resources allocated by this fixture.</summary>
        public void Dispose(){for(int index=owned.Count-1;index>=0;index--)owned[index].Dispose();}
        #endregion
        #endregion
        #region Private
        /// <summary>Authors a different output route and clear value for each target.</summary>
        private RHIRenderPassDescription Description(int index)=>FramebufferTestSetup.DescribeAttachments(targets[index],[new(AttachmentIndex: index, Load: AttachmentLoad.Clear,Clear:ColorClearValue.Float(.25f*(index+1),.5f,.75f,1))]);
        /// <summary>Creates a separate incoming native framebuffer image.</summary>
        private GpuFramebuffer CreateTarget(){var image=Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());return Own(DynamicGpuFramebuffer.CreateSingle(image)!);}
        /// <summary>Retains fixture allocation ownership in dependency order.</summary>
        private T Own<T>(T value)where T:IDisposable{owned.Add(value);return value;}
        #endregion
    }
    #endregion
}
