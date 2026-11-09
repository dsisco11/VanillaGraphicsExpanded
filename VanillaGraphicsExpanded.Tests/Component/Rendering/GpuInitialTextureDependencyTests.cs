using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks component admission when the initial buffer texture has no known backing.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuInitialTextureDependencyTests(HeadlessGLFixture fixture)
{
    #region Public API

    /// <summary>Ordinary acquisition cannot replace a maintained empty association with reader supplied facts.</summary>
    [Fact]
    public void UnavailableBackingPreventsInitialPublication()
    {
        // Arrange: bind an unbacked texture name while the custom reader would offer an invalid association.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        int texture = GL.GenTexture();
        try
        {
            GL.BindTexture(TextureTarget.TextureBuffer, texture);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var reader = new KnownAssociationReader(int.MaxValue);

            // Act: admit the texture through the resource manager's normal acquisition path.
            var entry = manager.AcquireResource(GpuResourceKind.Texture, texture, reader)!;

            // Assert: acquisition does not consult the rejected reader facts or invent backing metadata.
            Assert.Equal(0, reader.Reads);
            Assert.Null(entry.Metadata.Buffer);
            Assert.True(entry.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RHICommandContext.Current.RetireResource(new(GpuResourceKind.Texture, texture));
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion

    #region Private

    /// <summary>Provides a test association while recording whether discovery incorrectly reads it.</summary>
    private sealed class KnownAssociationReader(int buffer) : IGpuResourceMetadataReader<GpuTextureMetadata>
    {
        internal int Reads { get; private set; }

        #region Public API

        /// <inheritdoc />
        public bool IsResource(nint id) => GL.IsTexture((int)id);

        /// <inheritdoc />
        public bool TryRead(nint id, out GpuTextureMetadata value)
        {
            Reads++;
            value = new(TextureDimension.Buffer, ImmutableArray<GpuTextureLevelMetadata>.Empty,
                false, 0, 0, 0, new(buffer, TextureFormat.R32f, 0, 0, true));
            return true;
        }

        #endregion
    }

    #endregion
}
