using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks borrowed transfer rejection, backing deletion dependencies, and deferred shader retirement.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuResourceRetirementRegressionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Retired manager associations cannot retain immutable admission while wrapper cleanup is deferred.</summary>
    [Fact]
    public void DeletedFramebufferWithdrawsImmutableConfigurationBeforeWrapperDisposal()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var image = new GpuFramebufferAttachment(2,2,PixelInternalFormat.Rgba8);
            using var target = StaticGpuFramebuffer.Create([image]);
            int id = target.FboId;
            var cache = StateCache.Current;
            Assert.Throws<InvalidOperationException>(() => cache.RequireMutableFramebufferConfiguration(id));
            // Model confirmed external deletion arriving at the registry before deferred wrapper disposal.
            GL.DeleteFramebuffer(id);
            GpuResourceManagerSystem.CurrentManager!.ObserveDeletion(GpuResourceKind.Framebuffer,id);
            Assert.False(target.IsValid);
            cache.RequireMutableFramebufferConfiguration(id);
            Assert.False(cache.ValidateImmutableFramebufferRouting(id,[DrawBuffersEnum.ColorAttachment0]));
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Non-owning renderbuffer and framebuffer wrappers cannot transfer an external owner's identity.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NonOwningWrapRejectsExternalOwnerHandleTransfer(bool framebuffer, bool releaseHandle)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using GpuResource owner = framebuffer
                ? DynamicGpuFramebuffer.CreateEmpty()
                : RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            if (owner is GpuFramebuffer target) target.Bind();
            using GpuResource borrower = framebuffer
                ? DynamicGpuFramebuffer.Wrap(checked((int)owner.ResourceId), width: 4, height: 4)
                : VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLRenderbufferIntegration.Borrow(checked((int)owner.ResourceId));
            nint original = owner.ResourceId;
            Assert.Throws<NotSupportedException>(() => { if (releaseHandle) borrower.ReleaseHandle(); else borrower.Detach(); });
            Assert.Equal(original, owner.ResourceId);
            Assert.True(owner.IsValid);
            Assert.False(borrower.IsDisposed);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Backing-name deletion invalidates dependent metadata with or without a backing registration.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackingDeletionInvalidatesDependentMetadata(bool registerBacking)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int buffer = GL.GenBuffer();
        try
        {
            using (StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, buffer))
                GL.BufferData(BufferTarget.ArrayBuffer, 1024, IntPtr.Zero, BufferUsageHint.StaticDraw);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            if (registerBacking) Assert.NotNull(manager.AcquireResource(GpuResourceKind.Buffer, buffer, new GpuBufferMetadataReader()));
            else Assert.Null(manager.FindResource(GpuResourceKind.Buffer, buffer));
            using var view = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTextureIntegration.CreateBufferView(buffer, SizedInternalFormat.R32f, true, 0, 0, null).RequireResource();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(view));
            Assert.True(entry.MetadataAvailable);
            RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, buffer));
            Assert.False(GL.IsBuffer(buffer));
            Assert.Null(manager.FindResource(GpuResourceKind.Buffer, buffer));
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(buffer, view.BufferId);
            GL.GetTextureLevelParameter(view.TextureId, 0, (GetTextureParameter)All.TextureBufferDataStoreBindingExt, out int actualBuffer);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (GL.IsBuffer(buffer)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, buffer));
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>A delete-pending shader cannot regain a live canonical registration before physical destruction.</summary>
    [Fact]
    public void DeletePendingShaderCannotBeReacquiredAsLive()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int shader = GL.CreateShader(ShaderType.VertexShader);
        int program = GL.CreateProgram();
        GpuShaderModule? original = null;
        bool attached = false;
        try
        {
            GL.AttachShader(program, shader);
            attached = true;
            original = new GpuShaderModule(shader, ShaderType.VertexShader);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var retired = manager.GetResourceRegistration(original)!;
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            Assert.Equal(GpuResourceRegistrationState.Deleted, retired.State);
            Assert.True(GL.IsShader(shader));
            GL.GetShader(shader, ShaderParameter.DeleteStatus, out int pending);
            Assert.Equal(1, pending);
            Assert.Throws<InvalidOperationException>(() => new GpuShaderModule(shader, ShaderType.VertexShader));
            Assert.Null(manager.FindResource(GpuResourceKind.Shader, shader));
            GL.DetachShader(program, shader);
            attached = false;
            Assert.False(GL.IsShader(shader));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            original?.Dispose();
            if (attached && GL.IsProgram(program) && GL.IsShader(shader)) GL.DetachShader(program, shader);
            if (GL.IsProgram(program)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, program));
            if (GL.IsShader(shader)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    /// <summary>A current program marked for deletion cannot be adopted again while its native destruction is deferred.</summary>
    [Fact]
    public void DeletePendingCurrentProgramCannotBeReacquiredAsLive()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int shader = GL.CreateShader(ShaderType.VertexShader), program = GL.CreateProgram();
        GpuProgramObject? original = null;
        try
        {
            // Linking a minimal executable makes the program eligible to remain current after deletion.
            GL.ShaderSource(shader, "#version 430\nvoid main() { gl_Position = vec4(0.0); }");
            GL.CompileShader(shader);
            GL.AttachShader(program, shader);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.Equal(1, linked);
            original = GpuProgramObject.Adopt(program);
            GL.UseProgram(program);
            StateCache.Current.InvalidateAll();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var retired = manager.GetResourceRegistration(original)!;
            OpenGLRenderingDeletionCalls.Core.DeleteProgram(program);
            Assert.True(GL.IsProgram(program));
            GL.GetProgram(program, GetProgramParameterName.DeleteStatus, out int pending);
            Assert.Equal(1, pending);
            Assert.Equal(GpuResourceRegistrationState.Deleted, retired.State);
            Assert.Throws<InvalidOperationException>(() => GpuProgramObject.Adopt(program));
            Assert.Null(manager.FindResource(GpuResourceKind.Program, program));
            GL.UseProgram(0);
            Assert.False(GL.IsProgram(program));
            using var fresh = GpuProgramObject.Create();
            Assert.True(fresh.IsValid);
            Assert.NotSame(retired, manager.GetResourceRegistration(fresh));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.UseProgram(0);
            original?.Dispose();
            if (GL.IsProgram(program)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Program, program));
            if (GL.IsShader(shader)) VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Shader, shader));
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Framebuffer descriptors lose availability after backing-name retirement, regardless of backing registration.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FramebufferBackingDeletionInvalidatesDescriptors(bool renderbuffer, bool registered)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int backing = renderbuffer ? GL.GenRenderbuffer() : GL.GenTexture();
        int framebuffer = GL.GenFramebuffer();
        try
        {
            // Raw setup intentionally leaves the backing outside the manager in half of the cases.
            if (renderbuffer)
            {
                GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, backing);
                GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Rgba8, 4, 4);
            }
            else
            {
                GL.BindTexture(TextureTarget.Texture2D, backing);
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 4, 4, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            }
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            if (renderbuffer) GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, backing);
            else GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, backing, 0);
            StateCache.Current.InvalidateAll();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            if (registered)
            {
                if (renderbuffer) Assert.NotNull(manager.AcquireResource(GpuResourceKind.Renderbuffer, backing, GpuRenderbufferMetadataReader.Instance));
                else Assert.NotNull(manager.AcquireResource(GpuResourceKind.Texture, backing, GpuTextureMetadataReader.Instance));
            }
            var entry = manager.AcquireResource(GpuResourceKind.Framebuffer, framebuffer, GpuFramebufferMetadataReader.Instance)!;
            Assert.NotNull(entry);
            Assert.True(entry.MetadataAvailable);
            if (renderbuffer) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Renderbuffer, backing));
            else RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, backing));
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Framebuffer, framebuffer));
            if (renderbuffer && GL.IsRenderbuffer(backing)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Renderbuffer, backing));
            if (!renderbuffer && GL.IsTexture(backing)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Texture, backing));
            fixture.CleanupGpuResources();
            StateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
