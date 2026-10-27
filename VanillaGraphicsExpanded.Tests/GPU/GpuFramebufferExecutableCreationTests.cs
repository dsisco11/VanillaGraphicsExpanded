using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using System.Collections.Immutable;
using System.Reflection;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies framebuffer and executable creation publishes established facts without native descriptor discovery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferExecutableCreationTests(HeadlessGLFixture fixture)
{
    private static int queries;
    private static readonly HashSet<FramebufferAttachment> queriedSlots = new();

    #region Public API
    /// <summary>Explicit external recovery refreshes image storage even when the framebuffer's selected ID and mip are unchanged.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ForcedDiscoveryRefreshesUnchangedAttachmentBacking(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var framebuffer = DynamicGpuFramebuffer.CreateSingle(texture)!;
            var before = GpuFramebufferAttachmentDiscovery.Read(framebuffer, FramebufferAttachment.ColorAttachment0);
            Assert.Equal(4, before.Width);
            using (OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D, 0, texture.TextureId))
                GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba8, 8, 6, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            var recovered = GpuFramebufferAttachmentDiscovery.Read(framebuffer, FramebufferAttachment.ColorAttachment0, forceNative: true);
            Assert.Equal(texture.TextureId, recovered.TextureId);
            Assert.Equal(8, recovered.Width);
            Assert.Equal(6, recovered.Height);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local backing discovery cannot publish a partial texture or renderbuffer while its explicit mutation is unfinished.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalBackingAcquisitionRejectsUnfinishedOperation(bool renderbuffer)
    {
        fixture.EnsureContextValid();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        try
        {
            if (renderbuffer)
            {
                using var owner = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
                using var operation = new GpuResourceManager.MetadataOperation<GpuRenderbufferMetadata>(owner, GpuMetadataChange.Storage);
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireFramebufferRenderbuffer(owner.RenderbufferId, true));
            }
            else
            {
                using var owner = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
                using var operation = new GpuResourceManager.MetadataOperation<GpuTextureMetadata>(owner, GpuMetadataChange.Storage);
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.AcquireFramebufferTexture(owner.TextureId, TextureTarget.Texture2D, true));
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>External narrow edits query only the selected slot or default and retain every unrelated descriptor.</summary>
    [Fact]
    public void ExternalFramebufferEditsReadOnlyAffectedFields()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.ExternalFramebufferFields." + Guid.NewGuid());
        try
        {
            using var first = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var second = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            using var framebuffer = DynamicGpuFramebuffer.CreateSingle(first)!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(framebuffer));
            var original = entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment0];
            PatchQueries(harmony);
            queries = 0; queriedSlots.Clear();
            OpenGLFramebufferCalls.ExecuteCoreNamedFramebufferTexture(framebuffer.FboId, FramebufferAttachment.ColorAttachment1, second.TextureId, 0);
            Assert.True(queries > 0);
            Assert.Equal(FramebufferAttachment.ColorAttachment1, Assert.Single(queriedSlots));
            Assert.Equal(original, entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment0]);
            Assert.Equal(second.TextureId, entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment1].Id);
            if (GpuSupport.Graphics.SupportsFramebufferNoAttachments)
            {
                var attachments = entry.Metadata.Attachments;
                queries = 0; queriedSlots.Clear();
                OpenGLFramebufferCalls.ExecuteCoreNamedFramebufferParameter(framebuffer.FboId, FramebufferDefaultParameter.FramebufferDefaultWidth, 17);
                Assert.Equal(1, queries);
                Assert.Empty(queriedSlots);
                Assert.Equal(17, entry.Metadata.DefaultWidth);
                Assert.Same(attachments, entry.Metadata.Attachments);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Initial selections and later controlled replacement publish complete slots without scanning unrelated attachments.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KnownAttachmentsPublishWithoutDescriptorQueries(bool direct)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.KnownFramebufferAttachment." + Guid.NewGuid());
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
                Attachments = ImmutableDictionary<FramebufferAttachment, GpuFramebufferAttachment>.Empty
                    .Add(FramebufferAttachment.ColorAttachment0, firstImage)
            };
            PatchQueries(harmony);
            queries = 0;
            var created = GpuResourceManager.CreateFramebuffer(request, recipient, out _);
            Assert.NotNull(created);
            Assert.Equal(first.TextureId, created.Value.Attachments[FramebufferAttachment.ColorAttachment0].Id);
            Assert.Equal(0, queries);
            using var framebuffer = DynamicGpuFramebuffer.CreateEmpty();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(
                GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(framebuffer));
            framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0, firstImage);
            queries = 0;
            framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment1, secondImage);
            Assert.Equal(0, queries);
            Assert.Equal(first.TextureId, entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment0].Id);
            Assert.Equal(second.TextureId, entry.Metadata.Attachments[FramebufferAttachment.ColorAttachment1].Id);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
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
        var harmony = new Harmony("Tests.FramebufferExecutableCreation." + Guid.NewGuid());
        try
        {
            using var capabilities = new DirectStateAccessCapabilityScope(direct);
            using var sentinel = DynamicGpuFramebuffer.CreateEmpty();
            using var scope = OpenGlStateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, sentinel.FboId);
            PatchQueries(harmony);
            queries = 0;
            using var framebuffer = new Recipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            using var shader = new Recipient<GpuShaderMetadata>(GpuResourceKind.Shader);
            using var program = new Recipient<GpuProgramMetadata>(GpuResourceKind.Program);
            var frameData = GpuResourceManager.CreateFramebuffer(default, framebuffer, out var frameHandle);
            var shaderData = GpuResourceManager.CreateShader(new GpuShaderInitialization { Stage = ShaderType.VertexShader }, shader, out var shaderHandle);
            var programData = GpuResourceManager.CreateProgram(default, program, out var programHandle);
            Assert.Equal(0, queries);
            Assert.NotNull(frameData);
            Assert.Empty(frameData.Value.Attachments);
            Assert.Equal(0, frameData.Value.DefaultWidth);
            Assert.Equal(0, frameData.Value.DefaultSamples);
            Assert.False(frameData.Value.DefaultFixedSampleLocations);
            Assert.Equal(new GpuShaderMetadata(ShaderType.VertexShader), shaderData);
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
            Assert.Equal(1, queries);
            Assert.Equal((int)ShaderType.VertexShader, nativeStage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Omitted stage fails before allocation and explicit zero/false defaults remain distinct from omission.</summary>
    [Fact]
    public void InitializationRequiresStageAndTracksExplicitDefaults()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var shader = new Recipient<GpuShaderMetadata>(GpuResourceKind.Shader);
            Assert.Throws<ArgumentException>(() => GpuResourceManager.CreateShader(default, shader, out _));
            Assert.Equal((nint)0, shader.ResourceId);
            var omitted = new GpuFramebufferInitialization();
            Assert.False(omitted.HasDefaults);
            var explicitDefaults = new GpuFramebufferInitialization { DefaultSamples = 0, DefaultFixedSampleLocations = false };
            Assert.True(explicitDefaults.HasDefaults);
            using var framebuffer = new Recipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            // Attachmentless defaults are core baseline functionality. Invalid extents still reject before allocation.
            var invalid = new GpuFramebufferInitialization
                { DefaultWidth = checked(RHICommandContext.Current.Capabilities.MaxFramebufferWidth + 1) };
            Assert.Throws<ArgumentOutOfRangeException>(() => GpuResourceManager.CreateFramebuffer(invalid, framebuffer, out _));
            Assert.Equal((nint)0, framebuffer.ResourceId);
            var metadata = GpuResourceManager.CreateFramebuffer(explicitDefaults, framebuffer, out _);
            Assert.NotNull(metadata);
            Assert.Equal(0, metadata.Value.DefaultSamples);
            Assert.False(metadata.Value.DefaultFixedSampleLocations);
            using var omittedFramebuffer = new Recipient<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            Assert.NotNull(GpuResourceManager.CreateFramebuffer(omitted, omittedFramebuffer, out _));
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
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateFramebuffer(default, recipient, out handle));
                id = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            else if (family == 1)
            {
                using var recipient = new FailedRecipient<GpuShaderMetadata>(GpuResourceKind.Shader);
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateShader(new GpuShaderInitialization { Stage = ShaderType.VertexShader }, recipient, out handle));
                id = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            else
            {
                using var recipient = new FailedRecipient<GpuProgramMetadata>(GpuResourceKind.Program);
                Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateProgram(default, recipient, out handle));
                id = recipient.Attempted;
                Assert.Equal((nint)0, recipient.ResourceId);
            }
            Assert.NotEqual(0, id);
            Assert.Equal(default, handle);
            GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(family switch { 0 => GL.IsFramebuffer(id), 1 => GL.IsShader(id), _ => GL.IsProgram(id) });
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Counts exact integer descriptor overloads used by the corresponding native readers.</summary>
    private static void PatchQueries(Harmony harmony)
    {
        var prefix = new HarmonyMethod(typeof(GpuFramebufferExecutableCreationTests), nameof(CountQuery));
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetShader), [typeof(int), typeof(OpenTK.Graphics.OpenGL4.ShaderParameter), typeof(int).MakeByRefType()]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetProgram), [typeof(int), typeof(OpenTK.Graphics.OpenGL4.GetProgramParameterName), typeof(int).MakeByRefType()]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetProgramInterface), [typeof(int), typeof(OpenTK.Graphics.OpenGL4.ProgramInterface), typeof(OpenTK.Graphics.OpenGL4.ProgramInterfaceParameter), typeof(int).MakeByRefType()]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetFramebufferAttachmentParameter), [typeof(OpenTK.Graphics.OpenGL4.FramebufferTarget), typeof(OpenTK.Graphics.OpenGL4.FramebufferAttachment), typeof(OpenTK.Graphics.OpenGL4.FramebufferParameterName), typeof(int).MakeByRefType()]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetNamedFramebufferAttachmentParameter), [typeof(int), typeof(OpenTK.Graphics.OpenGL4.FramebufferAttachment), typeof(OpenTK.Graphics.OpenGL4.FramebufferParameterName), typeof(int).MakeByRefType()]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetFramebufferParameter), [typeof(OpenTK.Graphics.OpenGL4.FramebufferTarget), typeof(OpenTK.Graphics.OpenGL4.FramebufferDefaultParameter), typeof(int).MakeByRefType()]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.GetNamedFramebufferParameter), [typeof(int), typeof(OpenTK.Graphics.OpenGL4.FramebufferDefaultParameter), typeof(int).MakeByRefType()]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.IsShader), [typeof(int)]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.IsProgram), [typeof(int)]), prefix: prefix);
        harmony.Patch(AccessTools.Method(typeof(OpenTK.Graphics.OpenGL4.GL), nameof(GL.IsFramebuffer), [typeof(int)]), prefix: prefix);
    }

    /// <summary>Records a descriptor query without changing its native behavior.</summary>
    private static void CountQuery(MethodBase __originalMethod, object[] __args)
    {
        queries++;
        if (__originalMethod.Name.Contains("AttachmentParameter", StringComparison.Ordinal))
            queriedSlots.Add((FramebufferAttachment)Convert.ToInt32(__args[1]));
    }

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

