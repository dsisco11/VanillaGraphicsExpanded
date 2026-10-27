using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies native multiview descriptors, selected image projection and proven replay mode.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferMultiviewInterceptionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Common factories replay native multiview ranges, including view-relative selected layers, with split selectors preserved.</summary>
    [Theory]
    [InlineData(true,false)] [InlineData(false,false)] [InlineData(true,true)] [InlineData(false,true)]
    public void FactoryReplaysSelectedRange(bool direct,bool useView)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); GL.CreateTextures(TextureTarget.Texture2DArray,1,out int texture);
        try
        {
            GL.TextureStorage3D(texture,1,SizedInternalFormat.Rgba8,8,4,6);
            using var array=new BorrowedTexture(texture); using var view=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = array, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8, BaseMip = 0, MipCount = 1, BaseLayer = 1, LayerCount = 4, Label = null }).RequireResource();
            using var image=useView?GpuFramebufferAttachment.FromMultiviewTexture(view,1,2):GpuFramebufferAttachment.FromMultiviewTexture(array,1,2);
            using var capabilities=new DirectStateAccessCapabilityScope(direct);
            using var first=DynamicGpuFramebuffer.Create([image])!; using var target=DynamicGpuFramebuffer.Create([image])!;
            OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer,first.FboId); OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.ReadFramebuffer,target.FboId);
            target.SetAttachment(FramebufferAttachment.ColorAttachment0,image);
            Assert.Equal(first.FboId,GL.GetInteger(GetPName.DrawFramebufferBinding)); Assert.Equal(target.FboId,GL.GetInteger(GetPName.ReadFramebufferBinding));
            var entry=(GpuResourceMetadataEntry<GpuFramebufferMetadata>)GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(target)!;
            var descriptor=entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment0]; Assert.Equal(GpuFramebufferAttachmentMode.Multiview,descriptor.Mode); Assert.Equal(1,descriptor.BaseView); Assert.Equal(2,descriptor.ViewCount); Assert.Equal(useView?view.TextureId:array.TextureId,descriptor.Id);
            Assert.Equal(2,image.ImageDepth); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { RHICommandContext.Current.OpenGL.DeleteTexture(texture); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Unobserved initial attachment mode may expose native range facts but cannot invent replay semantics.</summary>
    [Fact]
    public void UnknownExternalModeRejectsReplay()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); GL.CreateTextures(TextureTarget.Texture2DArray,1,out int texture); GL.CreateFramebuffers(1,out int framebuffer);
        try
        {
            GL.TextureStorage3D(texture,1,SizedInternalFormat.Rgba8,8,4,4);
            using var array=new BorrowedTexture(texture);
            GL.Ovr.NamedFramebufferTextureMultiview(framebuffer,FramebufferAttachment.ColorAttachment0,texture,0,1,2);
            using var imported=DynamicGpuFramebuffer.Wrap(framebuffer); imported.PublishRenderPassMetadata();
            var image=imported.GetAttachment(FramebufferAttachment.ColorAttachment0)!; Assert.Equal(2,image.ImageDepth); Assert.Equal(GpuFramebufferAttachmentMode.Unknown,image.AttachmentMode);
            using var ordinary=GpuFramebufferAttachment.FromTexture(array,layer:0); using var target=DynamicGpuFramebuffer.Create([ordinary])!;
            Assert.Throws<NotSupportedException>(()=>image.AttachTo(target,FramebufferAttachment.ColorAttachment0)); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.OpenGL.DeleteFramebuffer(framebuffer); RHICommandContext.Current.OpenGL.DeleteTexture(texture); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    /// <summary>Backing storage publication refreshes selected extents and replays the proven OVR range.</summary>
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void MutableBackingResizePreservesRange(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); int texture=GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.Texture2DArray,texture); GL.TexImage3D(TextureTarget.Texture2DArray,0,PixelInternalFormat.Rgba8,8,4,4,0,PixelFormat.Rgba,PixelType.UnsignedByte,IntPtr.Zero);
            GpuResourceManagerSystem.CurrentManager!.AcquireResource(GpuResourceKind.Texture,texture,new GpuTextureMetadataReader(TextureTarget.Texture2DArray));
            using var array=new BorrowedTexture(texture); using var image=GpuFramebufferAttachment.FromMultiviewTexture(array,1,2); using var capabilities=new DirectStateAccessCapabilityScope(direct); using var framebuffer=DynamicGpuFramebuffer.Create([image])!;
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.Texture2DArray,0,texture);
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTexturesCalls.ExecuteCoreTexImage3D(TextureTarget.Texture2DArray, 0, PixelInternalFormat.Rgba8, 16, 8, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            framebuffer.Bind();
            Assert.Equal(16,image.Width); Assert.Equal(8,image.Height); Assert.Equal(2,image.ImageDepth);
            GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,(FramebufferParameterName)All.FramebufferAttachmentTextureBaseViewIndexOvr,out int first); GL.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer,FramebufferAttachment.ColorAttachment0,(FramebufferParameterName)All.FramebufferAttachmentTextureNumViewsOvr,out int count); Assert.Equal(1,first); Assert.Equal(2,count); Assert.Equal(ErrorCode.NoError,GL.GetError());
        }
        finally { RHICommandContext.Current.OpenGL.DeleteTexture(texture); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
}
