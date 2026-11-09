using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Integration tests for <see cref="GpuBindlessTextureHandle"/>.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuBindlessTextureHandleIntegrationTests
{
    private readonly HeadlessGLFixture fixture;

    #region Public API

    /// <summary>Uses the shared headless OpenGL fixture for bindless texture operations.</summary>
    public GpuBindlessTextureHandleIntegrationTests(HeadlessGLFixture fixture)
    {
        this.fixture = fixture;
    }

    /// <summary>Makes a texture handle resident and confirms disposal releases its residency.</summary>
    [Fact]
    public void CreateForTexture_MakeResidentAndDispose_TogglesResidency()
    {
        // Arrange
        fixture.MakeCurrent();

        Assert.SkipWhen(!GpuBindlessTextureHandle.IsSupported, "GL_ARB_bindless_texture not supported by this context.");

        using var tex = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var handle = GpuBindlessTextureHandle.CreateForTexture(tex, makeResident: true);

        // Assert
        Assert.NotEqual(0ul, handle.Handle);
        Assert.True(handle.IsResident);
        Assert.True(GL.Arb.IsTextureHandleResident(handle.Handle));

        ulong issued = handle.Handle;
        // Act
        handle.Dispose();

        // Assert
        Assert.False(GL.Arb.IsTextureHandleResident(issued));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
