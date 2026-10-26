using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Integration tests for <see cref="GpuBindlessTextureHandle"/>.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuBindlessTextureHandleIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    public GpuBindlessTextureHandleIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public void CreateForTexture_MakeResidentAndDispose_TogglesResidency()
    {
        fixture.MakeCurrent();

        Assert.SkipWhen(!GpuBindlessTextureHandle.IsSupported, "GL_ARB_bindless_texture not supported by this context.");

        using var tex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = RHITextureDimension.Two, Format = RHIFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = RHIStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var handle = GpuBindlessTextureHandle.CreateForTexture(tex, makeResident: true);

        Assert.NotEqual(0ul, handle.Handle);
        Assert.True(handle.IsResident);
        Assert.True(GL.Arb.IsTextureHandleResident(handle.Handle));

        handle.Dispose();

        Assert.False(GL.Arb.IsTextureHandleResident(handle.Handle));
    }
}
