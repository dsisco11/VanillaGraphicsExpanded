using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks metadata recovery while a texture operation owns the component entry.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuControlledTexturePublicationTests(HeadlessGLFixture fixture)
{
    #region Public API

    /// <summary>An active metadata operation prevents recovery from publishing a second snapshot.</summary>
    [Fact]
    public void ExplicitRecoveryRejectsActiveOperation()
    {
        // Arrange: create and register a texture, then hold its metadata entry in an active operation.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var texture = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription
            {
                Dimension = TextureDimension.Two,
                Format = TextureFormat.Rgba8,
                Width = System.Math.Max(1, 8),
                Height = System.Math.Max(1, 8),
                MipLevels = 4,
                Storage = ResourceStorageMode.Mutable,
                Filter = TextureFilterMode.Nearest,
                BaseLevel = 0,
                MaxLevel = (4) - 1,
                Label = null
            }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuTextureMetadata>>(manager.GetResourceRegistration(texture));
            ulong revision = entry.MetadataRevision;
            using var operation = new RHIResourceManager.MetadataOperation<GpuTextureMetadata>(texture, GpuMetadataChange.Metadata);

            // Act: request explicit recovery before the active operation releases ownership.
            var recovered = manager.RecoverResource(GpuResourceKind.Texture, texture.TextureId, GpuTextureMetadataReader.Instance);

            // Assert: recovery is refused, the revision remains fixed, and no native error was generated.
            Assert.Null(recovered);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.False(entry.MetadataAvailable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion
}
