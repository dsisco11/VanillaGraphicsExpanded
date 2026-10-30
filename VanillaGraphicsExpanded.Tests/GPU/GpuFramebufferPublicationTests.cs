using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks meaningful attachment publication independently of native command count or routing.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuFramebufferPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Identical configurations preserve consumers, while changed images and explicit publications notify once.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedAttachmentsPreserveDependentRevision(bool native)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var first = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba8, Width = 4, Height = 4, MipLevels = 1, BaseLevel = 0, MaxLevel = 0,
                Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest }).RequireResource();
            using var second = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba8, Width = 4, Height = 4, MipLevels = 1, BaseLevel = 0, MaxLevel = 0,
                Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest }).RequireResource();
            using var firstImage = GpuFramebufferAttachment.FromTexture(first);
            using var secondImage = GpuFramebufferAttachment.FromTexture(second);
            using var target = DynamicGpuFramebuffer.Create([firstImage]);
            using var alias = DynamicGpuFramebuffer.Wrap(target.FboId);
            alias.RefreshWrappedFramebuffer(target.FboId, 4, 4, publishPassMetadata: true);
            int publications = 0;
            alias.AttachmentsChanged += () => publications++;
            ulong originalRevision = alias.AttachmentRevision;

            // Repeated owner setup must not turn an unchanged mip selection into an image change.
            first.DisableMipmaps(); first.DisableMipmaps();
            Attach(firstImage); Attach(firstImage);
            Assert.Equal(0, publications);
            Assert.Equal(originalRevision, alias.AttachmentRevision);

            Attach(secondImage);
            Assert.Equal(1, publications);
            Assert.Equal(second.TextureId, alias.GetAttachment(FramebufferSlot.Color0)!.TextureId);
            Attach(secondImage);
            Assert.Equal(1, publications);

            // Engine rebuild publication is an explicit owner event even when native selections stay identical.
            alias.RefreshWrappedFramebuffer(target.FboId, 4, 4, publishPassMetadata: true);
            Assert.Equal(2, publications);
            Assert.True(first.IsValid); Assert.True(second.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());

            /// <summary>Exercises complete managed composition and observed native calls against the same known images.</summary>
            void Attach(GpuFramebufferAttachment image)
            {
                if (native) GL.NamedFramebufferTexture(target.FboId, FramebufferAttachment.ColorAttachment0, image.TextureId, 0);
                else Assert.True(OpenGLRHIContext.ConfigureEngineFramebuffer(target.FboId,
                    new Dictionary<FramebufferSlot, GpuFramebufferAttachment?> { [FramebufferSlot.Color0] = image }));
            }
        }
        finally { fixture.CleanupGpuResources(); }
    }
    #endregion
}
