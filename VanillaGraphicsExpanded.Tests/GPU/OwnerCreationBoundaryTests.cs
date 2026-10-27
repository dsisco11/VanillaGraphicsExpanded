using System;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks owner-allocation failure across the final RHI boundary without transferring borrowed storage.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class OwnerCreationBoundaryTests(HeadlessGLFixture fixture)
{
    private static int candidate;
    private static readonly InvalidOperationException failure = new("Injected owner initialization failure.");

    #region Public API
    /// <summary>Failure after native allocation cannot escape a usable owner or leave its candidate alive.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void FailedNativeInitializationRetiresOnlyCandidate(int family)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.OwnerAllocation." + Guid.NewGuid());
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new()
            {
                Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8,
                Width = 4, Height = 4, MipLevels = 1, Storage = ResourceStorageMode.Immutable
            }).RequireResource();
            using var image = GpuFramebufferAttachment.FromTexture(texture);
            candidate = 0;
            string method = family == 2 ? nameof(OpenGLBackend.InitializeRenderbuffer) : nameof(OpenGLBackend.CreateFramebuffer);
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), method),
                postfix: new HarmonyMethod(typeof(OwnerCreationBoundaryTests), nameof(FailAfterAllocation)));
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            {
                if (family == 0) { using var owner = DynamicGpuFramebuffer.Create([image]); }
                else if (family == 1) { using var owner = StaticGpuFramebuffer.Create([image]); }
                else { using var owner = new GpuFramebufferAttachment(RenderbufferStorage.DepthComponent24, 4, 4); }
            }));
            Assert.NotEqual(0, candidate);
            harmony.UnpatchAll(harmony.Id);
            GpuResourceManagerSystem.CurrentManager!.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(family == 2 ? GL.IsRenderbuffer(candidate) : GL.IsFramebuffer(candidate));
            Assert.True(texture.IsValid);
            Assert.True(image.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Failure after an attachment command retires its completed framebuffer while preserving caller-owned images.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedConfigurationPreservesBorrowedImages(bool immutable)
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.OwnerConfiguration." + Guid.NewGuid());
        try
        {
            using var image = new GpuFramebufferAttachment(4, 4, PixelInternalFormat.Rgba8);
            candidate = 0;
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), nameof(OpenGLBackend.ConfigureFramebufferAttachment)),
                postfix: new HarmonyMethod(typeof(OwnerCreationBoundaryTests), nameof(FailAfterAttachment)));
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            {
                if (immutable) { using var owner = StaticGpuFramebuffer.Create([image]); }
                else { using var owner = DynamicGpuFramebuffer.Create([image]); }
            }));
            harmony.UnpatchAll(harmony.Id);
            GpuResourceManagerSystem.CurrentManager!.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.NotEqual(0, candidate);
            Assert.False(GL.IsFramebuffer(candidate));
            Assert.True(image.IsValid);
            Assert.True(image.Resize(6, 6));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A failed selected-image projection returns newly created storage to its original cleanup lifetime.</summary>
    [Fact]
    public void FailedOwnedImageProjectionReleasesStorage()
    {
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.OwnerProjection." + Guid.NewGuid());
        try
        {
            candidate = 0;
            harmony.Patch(AccessTools.Method(typeof(GpuResourceManager), nameof(GpuResourceManager.AcquireImageMetadata)),
                prefix: new HarmonyMethod(typeof(OwnerCreationBoundaryTests), nameof(FailProjection)));
            Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            {
                using var image = new GpuFramebufferAttachment(RenderbufferStorage.DepthComponent24, 4, 4);
            }));
            harmony.UnpatchAll(harmony.Id);
            fixture.CleanupGpuResources();
            Assert.NotEqual(0, candidate);
            Assert.False(GL.IsRenderbuffer(candidate));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Captures an allocated candidate before manager publication and injects a truthful failure.</summary>
    private static void FailAfterAllocation(nint id) { candidate = checked((int)id); throw failure; }
    /// <summary>Injects a configuration interruption after the backend has committed the selected attachment.</summary>
    private static void FailAfterAttachment(GpuFramebuffer owner) { candidate = owner.FboId; throw failure; }
    /// <summary>Rejects projection after storage creation, before attachment ownership can be returned.</summary>
    private static void FailProjection(GpuFramebufferAttachment image) { candidate = image.RenderbufferId; throw failure; }
    #endregion
}
