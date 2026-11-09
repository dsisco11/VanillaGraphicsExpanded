using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks texture snapshot publication and source allocation dependencies.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuTextureMetadataPublicationTests(HeadlessGLFixture fixture)
{
    #region Public API


/// <summary>Controlled mip selection publishes supplied values without consulting an installed failed reader.</summary>
    [Fact]
    public void MipRangePublicationBypassesFailedReader()
    {
        // Arrange: Supply controlled mip metadata while the installed native metadata reader is set to fail.
        // Act and assert: Publish the selected range directly and verify supplied values are used without reader access.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var source = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(source));
            var original = entry.Reader;
            var failure = new FailedRead();
            try
            {
                entry.Reader = failure;
                source.SetMipRange(0, 0);
                Assert.Equal(0, failure.Reads);
                Assert.True(entry.MetadataAvailable);
                Assert.Equal(0, entry.Metadata.BaseLevel);
                Assert.Equal(0, entry.Metadata.MaxLevel);
            }
            finally { entry.Reader = original; }
            source.SetMipRange(0, 0);
            Assert.True(entry.MetadataAvailable);
            Assert.True(source.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }


    #endregion

    #region Private
    /// <summary>Declines one authoritative snapshot without altering native command execution.</summary>
    private sealed class FailedRead : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        public int Reads { get; private set; }
        /// <inheritdoc />
        public bool IsResource(nint id) => true;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata metadata)
        {
            Reads++;
            metadata = default;
            return false;
        }
    }
    #endregion
}
