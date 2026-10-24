using System.Runtime.CompilerServices;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises exact draw/read routing forwarding independently of framebuffer resource descriptors.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferRoutingInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Native route changes invalidate only cached routes and preserve split selections and queued errors.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    public void DrawRoutingOverloadsRecoverActualState(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = Patch(); GL.CreateFramebuffers(1,out int target); GL.CreateFramebuffers(1,out int sentinel);
        try
        {
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = manager.AcquireResource(GpuResourceKind.Framebuffer,target,GpuFramebufferMetadataReader.Instance)!;
            ulong revision = entry.MetadataRevision;
            var cache=StateCache.Current;
            cache.BindFramebuffer(FramebufferTarget.DrawFramebuffer,target);
            cache.BindFramebuffer(FramebufferTarget.ReadFramebuffer,sentinel);
            cache.SetDrawBuffer(DrawBufferMode.ColorAttachment0);
            _=cache.CaptureFramebufferDrawRouting(target);
            GL.Enable((EnableCap)(-1));
            DrawRoute(variant,target,DrawBuffersEnum.ColorAttachment1);
            Assert.Equal(DrawBuffersEnum.ColorAttachment1,cache.CaptureFramebufferDrawRouting(target)[0]);
            Assert.Equal(ErrorCode.InvalidEnum,GL.GetError());
            DrawRoute(variant,target,(DrawBuffersEnum)(-1));
            Assert.Equal(DrawBuffersEnum.ColorAttachment1,cache.CaptureFramebufferDrawRouting(target)[0]);
            Assert.NotEqual(ErrorCode.NoError,GL.GetError());
            Assert.Equal(target,GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(sentinel,GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(revision,entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); StateCache.Current.DeleteFramebuffer(target); StateCache.Current.DeleteFramebuffer(sentinel); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Read routing changes only the selected read buffer and leaves resource metadata and split bindings intact.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void ReadRoutingHasNoMetadataSideEffects(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony=Patch(); GL.CreateFramebuffers(1,out int target); GL.CreateFramebuffers(1,out int sentinel);
        try
        {
            var entry=GpuResourceManagerSystem.CurrentManager!.AcquireResource(GpuResourceKind.Framebuffer,target,GpuFramebufferMetadataReader.Instance)!;
            ulong revision=entry.MetadataRevision;
            StateCache.Current.BindFramebuffer(FramebufferTarget.ReadFramebuffer,target);
            StateCache.Current.BindFramebuffer(FramebufferTarget.DrawFramebuffer,sentinel);
            ReadRoute(variant,target);
            Assert.Equal((int)ReadBufferMode.ColorAttachment1,GL.GetInteger(GetPName.ReadBuffer));
            Assert.Equal(target,GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(sentinel,GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(revision,entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); StateCache.Current.DeleteFramebuffer(target); StateCache.Current.DeleteFramebuffer(sentinel); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Owned attachment publication invalidates other helpers while retaining the origin's owning image reference.</summary>
    [Fact]
    public void OwnedAttachmentUpdateNotifiesAliasWithoutReplacingOriginImage()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var first=new GpuFramebufferAttachment(4,4,PixelInternalFormat.Rgba8);
            using var replacement=new GpuFramebufferAttachment(8,4,PixelInternalFormat.Rgba8);
            using var owner=DynamicGpuFramebuffer.Create([first])!;
            using var alias=DynamicGpuFramebuffer.Wrap(owner.FboId);
            alias.PublishRenderPassMetadata();
            ulong revision=alias.AttachmentRevision;
            owner.SetAttachment(FramebufferAttachment.ColorAttachment0,replacement);
            Assert.True(alias.AttachmentRevision>revision);
            Assert.Same(replacement,owner.GetAttachment(FramebufferAttachment.ColorAttachment0));
            Assert.Equal(replacement.TextureId,alias.GetColorTextureId(0));
            Assert.Equal(8,alias.Width);
            alias.Bind(); owner.Bind();
            Assert.Same(replacement,owner.GetAttachment(FramebufferAttachment.ColorAttachment0));
            Assert.True(replacement.IsValid);
            Assert.True(first.IsValid);
            Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
    #region Private
    /// <summary>Installs production transpilation on the fixture's native routing boundaries.</summary>
    private static Harmony Patch()
    {
        var harmony=new Harmony("VGE.Tests.FramebufferRoutingInterception");
        foreach(string name in new[]{nameof(DrawRoute),nameof(ReadRoute)})
            harmony.Patch(AccessTools.Method(typeof(GpuFramebufferRoutingInterceptionTests),name),transpiler:new HarmonyMethod(typeof(EngineStateSwitchingHook),nameof(EngineStateSwitchingHook.Transpiler)));
        return harmony;
    }
    /// <summary>Forwards pointer/ref and named variants without copying away their signature distinctions.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static unsafe void DrawRoute(int variant,int framebuffer,DrawBuffersEnum route)
    {
        if(variant==0) GL.DrawBuffers(1,&route);
        else if(variant==1) GL.DrawBuffers(1,ref route);
        else if(variant==2) GL.NamedFramebufferDrawBuffer(framebuffer,(DrawBufferMode)route);
        else if(variant==3) GL.NamedFramebufferDrawBuffers(framebuffer,1,ref route);
        else if(variant==4) GL.Ext.FramebufferDrawBuffer(framebuffer,(DrawBufferMode)route);
        else { var value=(DrawBufferMode)route; GL.Ext.FramebufferDrawBuffers(framebuffer,1,ref value); }
    }
    /// <summary>Exercises bound, named and EXT read selectors without a metadata publication path.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ReadRoute(int variant,int framebuffer)
    {
        if(variant==0) GL.ReadBuffer(ReadBufferMode.ColorAttachment1);
        else if(variant==1) GL.NamedFramebufferReadBuffer(framebuffer,ReadBufferMode.ColorAttachment1);
        else GL.Ext.FramebufferReadBuffer(framebuffer,ReadBufferMode.ColorAttachment1);
    }
    #endregion
}
