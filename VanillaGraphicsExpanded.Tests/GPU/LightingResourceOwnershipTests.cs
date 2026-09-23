using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Guards actual GPU storage and lifetime at the production owners shared with lighting fixtures.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LightingResourceOwnershipTests : RenderTestBase
{
    /// <summary>Uses the mandatory shared OpenGL context.</summary>
    public LightingResourceOwnershipTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Allocation contracts
    /// <summary>Shared samplers cannot retain deleted context handles across normal shutdown and reinitialization.</summary>
    [Fact]
    public void SamplerShutdownRetiresHandlesAndAllowsFreshOwnership()
    {
        EnsureContextValid();
        var previous = GpuSamplers.NearestClamp;
        previous.Bind(0);
        int handle = previous.SamplerId;
        Assert.True(GL.IsSampler(handle));
        GpuSamplers.Dispose();
        Assert.False(previous.IsValid);
        Assert.False(GL.IsSampler(handle));
        var replacement = GpuSamplers.NearestClamp;
        Assert.NotSame(previous,replacement);
        replacement.Bind(0);
        Assert.True(GL.IsSampler(replacement.SamplerId));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Surface companions retain their image formats and retire together across different layer layouts.</summary>
    [Fact]
    public void SurfaceCompanionsShareLayoutAndRetireTogether()
    {
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
            textures.Dispose();
            Assert.All(layers, texture => Assert.False(texture.IsValid));
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Terrain identity remains integer storage while normal and material guides retain floating-point storage.</summary>
    [Fact]
    public void TerrainCompanionsOwnFormatsAndDisposal()
    {
        EnsureContextValid();
        using var textures = new GBufferTextures(3,5);
        AssertStorage(textures.Normal,TextureTarget.Texture2D,PixelInternalFormat.Rgba16f);
        AssertStorage(textures.Material,TextureTarget.Texture2D,PixelInternalFormat.Rgba16f);
        AssertStorage(textures.PatchId,TextureTarget.Texture2D,PixelInternalFormat.Rgba32ui);
        textures.Dispose();
        Assert.False(textures.Normal.IsValid); Assert.False(textures.Material.IsValid); Assert.False(textures.PatchId.IsValid);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Resizing and requested recreation retire old probe resources and produce complete replacement framebuffers.</summary>
    [Fact]
    public void ScreenOwnerRecreatesAllDependentTargets()
    {
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
        buffers.RequestRecreateBuffers("resource owner contract");
        Assert.False(buffers.EnsureBuffers(8,6));
        Assert.False(previous.IsValid);
        int savedFramebuffer = GpuFramebuffer.SaveBinding();
        try
        {
            buffers.ScreenProbeAtlasCurrentFbo!.Bind();
            Assert.Equal(FramebufferErrorCode.FramebufferComplete,GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer));
        }
        finally { GpuFramebuffer.RestoreBinding(savedFramebuffer); }
        buffers.Dispose();
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Reads driver storage rather than only trusting the allocation object's metadata.</summary>
    private static void AssertStorage(GpuTexture texture, TextureTarget target, PixelInternalFormat expected)
    {
        using var binding = GlStateCache.Current.BindTextureScope(target,0,texture.TextureId);
        GL.GetTexLevelParameter(target,0,GetTextureParameter.TextureInternalFormat,out int actual);
        Assert.Equal((int)expected,actual);
    }
    #endregion
}
