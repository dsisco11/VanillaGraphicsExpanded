using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies framebuffer and executable creation publishes established facts without native descriptor discovery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferExecutableCreationTests(HeadlessGLFixture fixture)
{

    #region Public API
    /// <summary>Maintained backing facts refresh an unchanged attachment selection without rediscovery.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MaintainedFactsRefreshUnchangedAttachmentBacking(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var framebuffer = DynamicGpuFramebuffer.CreateSingle(texture)!;
            var before = GpuFramebufferAttachmentDiscovery.Read(framebuffer, FramebufferSlot.Color0);
            Assert.Equal(4, before.Width);
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 6, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            var recovered = GpuFramebufferAttachmentDiscovery.Read(framebuffer, FramebufferSlot.Color0);
            Assert.Equal(texture.TextureId, recovered.TextureId);
            Assert.Equal(8, recovered.Width);
            Assert.Equal(6, recovered.Height);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Backing acquisition rejects a texture or renderbuffer while its explicit mutation is unfinished.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackingAcquisitionRejectsUnfinishedOperation(bool renderbuffer)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            if (renderbuffer)
            {
                using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
                using var operation = new RHIResourceManager.MetadataOperation<GpuRenderbufferMetadata>(owner, GpuMetadataChange.Storage);
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.AcquireFramebufferRenderbuffer(owner.RenderbufferId));
            }
            else
            {
                using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
                using var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(owner, GpuMetadataChange.Storage);
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.AcquireFramebufferTexture(owner.TextureId, TextureDimension.Two));
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>External edits publish supplied fields and retain unrelated descriptors without readback.</summary>
    [Fact]
    public void ExternalFramebufferEditsReadOnlyAffectedFields()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var framebuffer = DynamicGpuFramebuffer.CreateSingle(first)!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(framebuffer));
            var original = entry.Metadata.Attachments[FramebufferSlot.Color0];
            using var queries = new CreationDescriptorCapture();

            queries.Reset();
            GL.NamedFramebufferTexture(framebuffer.FboId, FramebufferAttachment.ColorAttachment1, second.TextureId, 0);
            Assert.Equal(0, queries.Reads);
            Assert.Empty(queries.QueriedSlots);
            Assert.Equal(original, entry.Metadata.Attachments[FramebufferSlot.Color0]);
            Assert.Equal(second.TextureId, entry.Metadata.Attachments[FramebufferSlot.Color1].Id);
            if (GpuSupport.Graphics.SupportsFramebufferNoAttachments)
            {
                var attachments = entry.Metadata.Attachments;
                queries.Reset();
                GL.NamedFramebufferParameter(framebuffer.FboId, FramebufferDefaultParameter.FramebufferDefaultWidth, 17);
                Assert.Equal(0, queries.Reads);
                Assert.Empty(queries.QueriedSlots);
                    Assert.Equal(17, entry.Metadata.DefaultWidth);
                Assert.Same(attachments, entry.Metadata.Attachments);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Initial selections and later controlled replacement publish complete slots without scanning unrelated attachments.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownAttachmentsPublishWithoutDescriptorQueries(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(direct);
            using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var firstImage = GpuFramebufferAttachment.FromTexture(first);
            using var secondImage = GpuFramebufferAttachment.FromTexture(second);
            using var recipient = new Recipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            var request = new GpuFramebufferInitialization
            {
                Attachments = ImmutableDictionary<FramebufferSlot, GpuFramebufferAttachment>.Empty
                    .Add(FramebufferSlot.Color0, firstImage)
            };
            using var queries = new CreationDescriptorCapture();
            queries.Reset();
            var created = RHIResourceManager.CreateFramebuffer(request, recipient, out _);
            Assert.NotNull(created);
            Assert.Equal(first.TextureId, created.Value.Attachments[FramebufferSlot.Color0].Id);
            Assert.Equal(0, queries.Reads);
            using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(
                RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(framebuffer));
            framebuffer.SetAttachment(FramebufferSlot.Color0, firstImage);
            queries.Reset();
            framebuffer.SetAttachment(FramebufferSlot.Color1, secondImage);
            Assert.Equal(0, queries.Reads);
            Assert.Equal(first.TextureId, entry.Metadata.Attachments[FramebufferSlot.Color0].Id);
            Assert.Equal(second.TextureId, entry.Metadata.Attachments[FramebufferSlot.Color1].Id);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Fresh empty descriptors and fixed shader stages reach registered and local recipients without queries.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CreationPublishesEstablishedFactsWithoutQueries(bool registered, bool direct)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(direct);
            using var sentinel = DynamicGpuFramebuffer.CreateEmpty();
            using var scope = OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, sentinel.FboId);
            using var queries = new CreationDescriptorCapture();
            queries.Reset();
            using var framebuffer = new Recipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            using var shader = new Recipient<GpuShaderMetadata>(GpuResourceKind.Shader);
            using var program = new Recipient<GpuProgramMetadata>(GpuResourceKind.Program);
            var frameData = RHIResourceManager.CreateFramebuffer(default, framebuffer, out var frameHandle);
            var shaderData = RHIResourceManager.CreateShader(new GpuShaderInitialization { Stage = ShaderStage.Vertex }, shader, out var shaderHandle);
            var programData = RHIResourceManager.CreateProgram(default, program, out var programHandle);
            Assert.Equal(0, queries.Reads);
            Assert.NotNull(frameData);
            Assert.Empty(frameData.Value.Attachments);
            Assert.Equal(0, frameData.Value.DefaultWidth);
            Assert.Equal(0, frameData.Value.DefaultSamples);
            Assert.True(frameData.Value.DefaultFixedSampleLocations);
            Assert.Equal(new GpuShaderMetadata(ShaderStage.Vertex), shaderData);
            Assert.NotNull(programData);
            Assert.False(programData.Value.ExecutableAvailable);
            Assert.Empty(programData.Value.Resources);
            Assert.Empty(programData.Value.TextureUnits);
            Assert.Equal(frameData.Value, framebuffer.Snapshot);
            Assert.Equal(shaderData.Value, shader.Snapshot);
            Assert.Equal(programData.Value, program.Snapshot);
            Assert.Equal(frameHandle.Id, framebuffer.ResourceId);
            Assert.Equal(shaderHandle.Id, shader.ResourceId);
            Assert.Equal(programHandle.Id, program.ResourceId);
            Assert.Equal(sentinel.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(sentinel.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            // Calibrate the concrete intercepted entry point after the measured interval.
            OpenTK.Graphics.OpenGL4.GL.GetShader((int)shaderHandle.Id, OpenTK.Graphics.OpenGL4.ShaderParameter.ShaderType, out int nativeStage);
            Assert.Equal(1, queries.Reads);
            Assert.Equal((int)ShaderType.VertexShader, nativeStage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Omitted stage fails before allocation and explicit zero/false defaults remain distinct from omission.</summary>
    [Fact]
    public void InitializationRequiresStageAndTracksExplicitDefaults()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var shader = new Recipient<GpuShaderMetadata>(GpuResourceKind.Shader);
            Assert.Throws<ArgumentException>(() => RHIResourceManager.CreateShader(default, shader, out _));
            Assert.Equal((nint)0, shader.ResourceId);
            var omitted = new GpuFramebufferInitialization();
            Assert.False(omitted.HasDefaults);
            var explicitDefaults = new GpuFramebufferInitialization { DefaultSamples = 0, DefaultFixedSampleLocations = false };
            Assert.True(explicitDefaults.HasDefaults);
            using var framebuffer = new Recipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            // Attachmentless defaults are core baseline functionality. Invalid extents still reject before allocation.
            var invalid = new GpuFramebufferInitialization
                { DefaultWidth = checked(RHICommandContext.Current.Capabilities.MaxFramebufferWidth + 1) };
            Assert.Throws<ArgumentOutOfRangeException>(() => RHIResourceManager.CreateFramebuffer(invalid, framebuffer, out _));
            Assert.Equal((nint)0, framebuffer.ResourceId);
            var metadata = RHIResourceManager.CreateFramebuffer(explicitDefaults, framebuffer, out _);
            Assert.NotNull(metadata);
            Assert.Equal(0, metadata.Value.DefaultSamples);
            Assert.False(metadata.Value.DefaultFixedSampleLocations);
            using var omittedFramebuffer = new Recipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            Assert.NotNull(RHIResourceManager.CreateFramebuffer(omitted, omittedFramebuffer, out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Failed initial delivery retires each provisional family identity without exposing a successful handle.</summary>
    [Theory]
    [InlineData(false, 0)] [InlineData(false, 1)] [InlineData(false, 2)]
    [InlineData(true, 0)] [InlineData(true, 1)] [InlineData(true, 2)]
    public void FailedHandoffRetiresNativeIdentity(bool registered, int family)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            int id;
            GpuResourceHandle handle = default;
            if (family == 0)
            {
                using var recipient = new FailedRecipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateFramebuffer(default, recipient, out handle));
                id = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            else if (family == 1)
            {
                using var recipient = new FailedRecipient<GpuShaderMetadata>(GpuResourceKind.Shader);
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateShader(new GpuShaderInitialization { Stage = ShaderStage.Vertex }, recipient, out handle));
                id = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            else
            {
                using var recipient = new FailedRecipient<GpuProgramMetadata>(GpuResourceKind.Program);
                Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateProgram(default, recipient, out handle));
                id = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            Assert.NotEqual(0, id);
            Assert.Equal(default, handle);
            RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.False(family switch { 0 => GL.IsFramebuffer(id), 1 => GL.IsShader(id), _ => GL.IsProgram(id) });
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Receives the initial canonical snapshot through the real manager association boundary.</summary>
    private sealed class Recipient<T>(GpuResourceKind kind) : GpuResource<T> where T : struct
    {
        internal T Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }

    /// <summary>Rejects initial identity delivery after allocation to exercise actual family cleanup.</summary>
    private sealed class FailedRecipient<T>(GpuResourceKind kind) : GpuResource<T> where T : struct
    {
        private nint id;
        internal int Attempted;
        /// <inheritdoc />
        public override nint ResourceId
        {
            get => id;
            protected set
            {
                id = value;
                if (value != 0) { Attempted = (int)value; throw new InvalidOperationException("Injected identity handoff failure."); }
            }
        }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}

