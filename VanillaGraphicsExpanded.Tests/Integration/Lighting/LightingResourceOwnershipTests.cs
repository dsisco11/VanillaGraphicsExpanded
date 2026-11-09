using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Lighting;

/// <summary>Guards actual GPU storage and lifetime at the production owners shared with lighting fixtures.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LightingResourceOwnershipTests : RenderTestBase
{
    /// <summary>Uses the mandatory shared OpenGL context.</summary>
    public LightingResourceOwnershipTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Allocation contracts
    /// <summary>Shared samplers cannot retain deleted context handles across normal shutdown and reinitialization.</summary>
    [Fact]
    public void SamplerShutdownRetiresHandlesAndAllowsFreshOwnership()
    {
        // Arrange: Bind the shared sampler and retain its native handle before shutting down its owner.
        EnsureContextValid();
        var previous = GpuSamplers.NearestClamp;
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, 0, previous.SamplerId);
        int handle = previous.SamplerId;
        Assert.True(GL.IsSampler(handle));
        // Act: Retire the shared set and request the lazy replacement.
        GpuSamplers.Dispose();
        Assert.False(previous.IsValid);
        Assert.False(GL.IsSampler(handle));
        var replacement = GpuSamplers.NearestClamp;
        Assert.NotSame(previous,replacement);
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, 0, replacement.SamplerId);
        // Assert: The old handle is gone and the new owner publishes a distinct live handle.
        Assert.True(GL.IsSampler(replacement.SamplerId));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Surface companions retain their image formats and retire together across different layer layouts.</summary>
    [Fact]
    public void SurfaceCompanionsShareLayoutAndRetireTogether()
    {
        // Arrange: Create companion atlases at two extents to exercise shared allocation layout.
        EnsureContextValid();
        foreach (int size in new[] { 8, 16 })
        {
            using var textures = new SurfaceAtlasTextures(size,size,2,"Tests.OwnerContract");
            GpuTexture[] layers = [textures.Depth,textures.Material,textures.Indirect,textures.Direct,..textures.Outgoing];
            PixelInternalFormat[] formats = [PixelInternalFormat.R16f,PixelInternalFormat.Rgba8,
                PixelInternalFormat.Rgba16f,PixelInternalFormat.Rgba16f,PixelInternalFormat.Rgba16f,PixelInternalFormat.Rgba16f];
            for (int i=0;i<layers.Length;i++)
            {
                Assert.Equal((size,size,2),(layers[i].Width,layers[i].Height,layers[i].Depth));
                AssertStorage(layers[i],TextureTarget.Texture2DArray,formats[i]);
            }
            // Act: Retire the owner after checking each attachment against driver storage.
            textures.Dispose();
            // Assert: Every borrowed layer reference is invalidated together.
            Assert.All(layers, texture => Assert.False(texture.IsValid));
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Terrain identity remains integer storage while normal and material guides retain floating-point storage.</summary>
    [Fact]
    public void TerrainCompanionsOwnFormatsAndDisposal()
    {
        // Arrange: Allocate the production G-buffer companion set.
        EnsureContextValid();
        using var textures = new GBufferTextures(3,5);
        AssertStorage(textures.Surface,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba16f);
        Assert.Equal(3, textures.Surface.Depth);
        AssertStorage(textures.PatchId,TextureTarget.Texture2D,PixelInternalFormat.Rgba32ui);
        // Act: Dispose the set after querying the native formats.
        textures.Dispose();
        // Assert: Both owned images are retired without GL errors.
        Assert.False(textures.Surface.IsValid); Assert.False(textures.PatchId.IsValid);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Resizing and requested recreation retire old probe resources and produce complete replacement framebuffers.</summary>
    [Fact]
    public void ScreenOwnerRecreatesAllDependentTargets()
    {
        // Arrange: Build the production LumOn target owner with its smallest supported probe spacing.
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        var config = new VgeConfig(); config.LumOn.ProbeSpacingPx=2;
        using var buffers = new LumOnBufferManager(assets.Api,config);
        Assert.False(buffers.EnsureBuffers(4,4));
        Assert.True(buffers.EnsureBuffers(4,4));
        var previous = buffers.ScreenProbeAtlasCurrentTex!;
        long revision = buffers.HistoryRevision;
        Assert.False(buffers.EnsureBuffers(8,6));
        Assert.False(previous.IsValid);
        Assert.True(buffers.HistoryRevision>revision);
        Assert.Equal((8,6),(buffers.IndirectFullTex!.Width,buffers.IndirectFullTex.Height));
        Assert.Equal((32,24),(buffers.ScreenProbeAtlasCurrentTex!.Width,buffers.ScreenProbeAtlasCurrentTex.Height));
        previous = buffers.ScreenProbeAtlasCurrentTex;
        // Act: Resize, request same-size recreation, and realize the replacement pass inside its declared boundary.
        buffers.RequestRecreateBuffers("resource owner contract");
        Assert.False(buffers.EnsureBuffers(8,6));
        Assert.False(previous.IsValid);
        var context=VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current;
        using(var boundary=context.EnterBoundary(new("Tests.LumOn.TargetCompletion",true,operations:VanillaGraphicsExpanded.Rendering.RHI.BoundaryOperations.RenderPasses)))
            boundary.Run(()=>
            {
                context.BeginRenderPass(buffers.ScreenProbeAtlasCurrentPass!);
                Assert.Equal(FramebufferErrorCode.FramebufferComplete,GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
                context.EndRenderPass();
            });
        // Assert: Recreation retires prior storage, updates dimensions, and leaves the replacement pass complete.
        buffers.Dispose();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Reads driver storage rather than only trusting the allocation object's metadata.</summary>
    private static void AssertStorage(GpuTexture texture, TextureTarget target, PixelInternalFormat expected)
    {
        using var binding = OpenGlStateCache.Current.BindTextureScope(target,0,texture.TextureId);
        GL.GetTexLevelParameter(target,0,GetTextureParameter.TextureInternalFormat,out int actual);
        Assert.Equal((int)expected,actual);
    }
    #endregion
}
