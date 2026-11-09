using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using Native = OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Qualifies core named framebuffer routing, repeated restoration and independent cleanup.</summary>
[Collection("GPU")][Trait("Category", "GpuIntegration")]
public sealed class FramebufferRoutingDsaTests(HeadlessGLFixture fixture,ITestOutputHelper output):RenderTestBase(fixture)
{


    #region Public API
    #region Equivalent restoration
    /// <summary>Repeated named restoration preserves images and routes in standalone and shared boundaries.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void NamedRestorationProducesEquivalentResults(bool standalone)
    {
        // Arrange
        EnsureContextValid();Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        // Clear-only passes declare no executable; preserve any program left by another shared-fixture test.
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
        using var scene=new RoutingScene();scene.Initialize();List<string> fallback;
        // Act: compare fallback publication with named restoration from the same initialized scene.
        using(var capture=new GraphicsTransitionCapture()){scene.Render(standalone);fallback=[..capture.Calls];}
        scene.AssertRestored();using var expected=scene.CaptureOutput();scene.Initialize();List<string> named;
        using(var capture=new GraphicsTransitionCapture()){scene.Render(standalone);named=[..capture.Calls];}
        scene.AssertRestored();using var actual=scene.CaptureOutput();
        // Assert
        for(int layer=0;layer<expected.Count;layer++)ImageAssertions.Comparisons.CompareHistory(actual[layer].Texture, expected[layer]).AssertEqual();
        Assert.Equal(.25f,Assertions.GpuLocalRead.Scalar(actual[0].Texture, 0));Assert.Equal(.5f,Assertions.GpuLocalRead.Scalar(actual[3].Texture, 0));
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
        // Arrange
        EnsureContextValid();Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        // Clear-only passes declare no executable; preserve any program left by another shared-fixture test.
        OpenGLProgramSelection.Select(OpenGlStateCache.Current,0);
        using var capability=new DirectStateAccessCapabilityScope(directStateAccess);
        using var originalBindings=OpenGlStateCache.Current.BindFramebufferScope();
        using var window=DynamicGpuFramebuffer.Wrap(0,width:1,height:1);window.PublishSurfaceMetadata(1,1);
        var metadata=window.Surface!;
        DrawBufferMode grouped=metadata.Buffer==SurfaceColorBuffer.BackLeft?DrawBufferMode.Back:DrawBufferMode.Front;
        int previousDrawBuffer = 0;
        int previousReadBuffer = 0;
        bool hasPreviousDrawBuffer = false;
        bool hasPreviousReadBuffer = false;
        GpuFramebuffer? incoming = null;
        // Act: render through the pass boundary and verify restoration of the grouped window selector.
        try
        {
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer,0);
            previousDrawBuffer = GL.GetInteger(GetPName.DrawBuffer0);
            hasPreviousDrawBuffer = true;
            GL.DrawBuffer(grouped);
            OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);

            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer,0);
            previousReadBuffer = GL.GetInteger(GetPName.ReadBuffer);
            hasPreviousReadBuffer = true;

            incoming = CreateRenderTarget(1,1,TextureFormat.Rgba32f);
            incoming.Bind();
            using(var capture=new GraphicsTransitionCapture())
            {
                {
    var commands=RHICommandContext.Current;
    using var scope44=commands.EnterBoundary(new("Tests.Dsa.Window",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope44.Run(()=>{
                    commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(window, [new(Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(.25f,.5f,.75f,1),WindowBuffer:metadata.Buffer==SurfaceColorBuffer.BackLeft?SurfaceColorBuffer.BackLeft:SurfaceColorBuffer.FrontLeft)]));commands.EndRenderPass();
                });
}
                Assert.Contains(capture.Calls,call=>call==$"RoutingWindow:0:{grouped}");
                Assert.DoesNotContain(capture.Calls,call=>call.StartsWith("RoutingNamed:"));
                output.WriteLine("WINDOW_ROUTING_DSA="+System.Text.Json.JsonSerializer.Serialize(new{directStateAccess,grouped=grouped.ToString(),calls=capture.Calls}));
            }
            // Assert
            Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding));
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer,0);Assert.Equal((int)grouped,GL.GetInteger(GetPName.DrawBuffer0));
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer,0);GL.ReadBuffer(metadata.Buffer == SurfaceColorBuffer.BackLeft ? ReadBufferMode.BackLeft : ReadBufferMode.FrontLeft);
            using var pack=OpenGLPixelLayouts.PackScope(OpenGlStateCache.Current, new(1));byte[] pixel=new byte[4];GL.ReadPixels(0,0,1,1,PixelFormat.Rgba,PixelType.UnsignedByte,pixel);
            Assert.InRange(pixel[0],(byte)63,(byte)65);
        }
        finally
        {
            if (hasPreviousDrawBuffer)
            {
                OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer,0);
                GL.DrawBuffer((DrawBufferMode)previousDrawBuffer);
            }

            OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);

            if (hasPreviousReadBuffer)
            {
                OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer,0);
                GL.ReadBuffer((ReadBufferMode)previousReadBuffer);
            }

            OpenGlStateCache.Current.InvalidateAll();
            incoming?.Dispose();
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #region Independent cleanup
    /// <summary>A failed named restore cannot prevent another target's routing or the enclosing native bindings from being restored.</summary>
    [Fact]
    public void NamedRestoreFailureStillCleansRemainingOwners()
    {
        // Arrange
        EnsureContextValid();Assert.True(GpuSupport.Graphics.SupportsArbDirectStateAccess);
        // Clear-only passes declare no executable; preserve any program left by another shared-fixture test.
        OpenGLProgramSelection.Select(OpenGlStateCache.Current,0);
        using var scene=new RoutingScene();scene.Initialize();

        // Act: inject failure for one owner's route and allow restoration to continue for another owner.
        using(var capture=new GraphicsTransitionCapture())
        {
            capture.FailNamedFramebuffer=scene.FirstFramebuffer;
            Exception failure;
            try { failure=Assert.ThrowsAny<Exception>(()=>scene.Render(false)); }
            catch
            {
                // Keep the original expected-failure assertion and retain actual dispatch
                // evidence when attribution is missing; observations run after rendering.
                output.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    target=scene.FirstFramebuffer,capture.SelectedTargetRoutingCalls,
                    capture.InjectedRoutingErrors,capture.DriverErrorCount,calls=capture.Calls
                }));
                throw;
            }
            // Assert
            Assert.Equal(2,capture.SelectedTargetRoutingCalls);
            Assert.Equal(1,capture.InjectedRoutingErrors);
            Assert.Equal(0,capture.DriverErrorCount);
            Assert.Contains("After Framebuffer routing publication",failure.ToString());
            Assert.Contains(nameof(ErrorCode.InvalidOperation),failure.ToString());
        }
        scene.AssertRestored(includeFirst:false);scene.AssertReleased();Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>The simulated restoration error cannot hide a genuine driver error or cancel named dispatch.</summary>
    [Fact]
    public void NativeErrorInjectionPreservesRealErrorsAndDispatch()
    {
        // Arrange
        EnsureContextValid();
        using var scene=new RoutingScene();scene.Initialize();
        using(var capture=new GraphicsTransitionCapture())
        {
            capture.FailNamedFramebuffer=scene.FirstFramebuffer;
            try
            {
                // Act: queue a real driver error before the injected routing error.
                Native.GL.NamedFramebufferDrawBuffer(scene.FirstFramebuffer,Native.DrawBufferMode.ColorAttachment0);
                Native.GL.NamedFramebufferDrawBuffers(scene.FirstFramebuffer,3,
                    [Native.DrawBuffersEnum.None,Native.DrawBuffersEnum.ColorAttachment1,Native.DrawBuffersEnum.ColorAttachment0]);
                GL.LineWidth(-1f); // A real InvalidValue must precede the pending simulated error.
                // Assert
                Assert.Equal(ErrorCode.InvalidValue,GL.GetError());
                Assert.Equal(ErrorCode.InvalidOperation,GL.GetError());
                Assert.Equal(ErrorCode.NoError,GL.GetError());
                Assert.Equal(2,capture.SelectedTargetRoutingCalls);
                Assert.Equal(1,capture.InjectedRoutingErrors);
                Assert.Equal(1,capture.DriverErrorCount);
            }
            finally
            {
                // Contain this negative test's intentional error state even if an assertion fails.
                for(int pending=0;pending<4&&GL.GetError()!=ErrorCode.NoError;pending++){}
            }
        }
        scene.AssertRestored();scene.AssertReleased();Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
    #endregion

    #region Private Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
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

        /// <summary>Releases all resources allocated by this scene, continuing if an individual release fails.</summary>
        public void Dispose() => DisposeOwnedResources();

        #endregion

        #region Internal
        #region Setup and submission
        /// <summary>Creates independently owned images and framebuffers without any shader or legacy pipeline fixture.</summary>
        internal RoutingScene()
        {
            try
            {
                images = Enumerable.Range(0, 4)
                    .Select(_ => Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
                    {
                        Dimension = TextureDimension.Two,
                        Format = TextureFormat.Rgba32f,
                        Width = System.Math.Max(1, 2),
                        Height = System.Math.Max(1, 2),
                        MipLevels = 1,
                        Storage = ResourceStorageMode.Mutable,
                        Filter = TextureFilterMode.Nearest,
                        BaseLevel = 0,
                        MaxLevel = (1) - 1,
                        Label = null
                    }).RequireResource()))
                    .ToArray();
                targets =
                [
                    Own(DynamicGpuFramebuffer.CreateMRT(images.Take(2).ToArray())!),
                    Own(DynamicGpuFramebuffer.CreateMRT(images.Skip(2).ToArray())!)
                ];

                // Model engine-owned framebuffer surfaces so pass handback concerns the original IDs.
                targets = targets.Select(target =>
                {
                    var external = Own(DynamicGpuFramebuffer.Wrap(target.FboId));
                    external.RefreshWrappedFramebuffer(target.FboId, 2, 2, publishPassMetadata: true);
                    return (GpuFramebuffer)external;
                }).ToArray();

                incoming = CreateTarget();
                read = CreateTarget();
            }
            catch (Exception initializationFailure)
            {
                try
                {
                    DisposeOwnedResources();
                }
                catch (Exception cleanupFailure)
                {
                    throw new AggregateException("Routing scene initialization and rollback both failed.",
                        initializationFailure, cleanupFailure);
                }

                throw;
            }
        }
        /// <summary>Identifies the first cleanup owner for controlled failure injection.</summary>
        internal int FirstFramebuffer=>targets[0].FboId;
        /// <summary>Seeds exact route history and neutral image contents before either compared execution.</summary>
        internal void Initialize()
        {
            foreach(var image in images)image.UploadDataImmediate(new float[16]);
            for(int index=0;index<targets.Length;index++){targets[index].Bind();GL.DrawBuffers(Original[index].Length,Original[index]); OpenGlStateCache.Current.Invalidate(EPipelineState.FramebufferRouting);}
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer,incoming.FboId);OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.ReadFramebuffer,read.FboId);
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
                OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer,targets[index].FboId);
                for(int slot=0;slot<Original[index].Length;slot++)Assert.Equal((int)Original[index][slot],GL.GetInteger(GetPName.DrawBuffer0+slot));
            }
            OpenGLFramebufferSelection.Bind(OpenGlStateCache.Current, FramebufferTarget.DrawFramebuffer,incoming.FboId);
        }
        /// <summary>Reads physical outputs after capture without changing the compared operation count.</summary>
        internal Assertions.GpuImageSet CaptureOutput() => new(images);
        /// <summary>Requires every deferred framebuffer lifetime and active image borrow to have ended.</summary>
        internal void AssertReleased(){foreach(var image in images)Assert.True(image.Resize(3,3));foreach(var target in targets)target.Dispose();}
        #endregion
        #endregion
        #region Private
        /// <summary>Disposes owned resources in reverse acquisition order and reports any cleanup failures.</summary>
        private void DisposeOwnedResources()
        {
            List<Exception>? failures = null;
            for (int index = owned.Count - 1; index >= 0; index--)
            {
                try
                {
                    owned[index].Dispose();
                }
                catch (Exception failure)
                {
                    (failures ??= []).Add(failure);
                }
            }

            owned.Clear();
            if (failures is not null)
            {
                throw new AggregateException("One or more routing scene resources could not be released.", failures);
            }
        }

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
