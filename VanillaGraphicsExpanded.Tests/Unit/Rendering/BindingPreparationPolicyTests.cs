using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies prepared binding tables retain the context lifetime that first authorized them.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class BindingPreparationPolicyTests(HeadlessGLFixture fixture)
{
    #region Public API

    /// <summary>Reusing a prepared table cannot replace its original context authority with another admitted context.</summary>
    [Fact]
    public void PreparedStructureKeepsItsOriginalContextAuthority()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();

        try
        {
            var context = RHICommandContext.Current;
            using var shader = context.CreateShader(RHIShaderInputs.EmptyComputeDescription()).RequireResource();
            var bindings = new RHIResourceBindings(shader);
            var origin = new ContextLifetime();

            // Act
            bindings.Prepare(context.Capabilities, origin);
            origin.Retired = true;

            // Assert
            Assert.Throws<InvalidOperationException>(() => bindings.Prepare(context.Capabilities, new ContextLifetime()));

            // Act
            RHISampledTextureBindings sampled = [];
            origin.Retired = false;
            sampled.Prepare(context.Capabilities, origin);
            origin.Retired = true;

            // Assert
            Assert.Throws<InvalidOperationException>(() => sampled.Prepare(context.Capabilities, new ContextLifetime()));
        }
        finally
        {
            fixture.CleanupGpuResources();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion

    #region Private

    /// <summary>Models the opaque context lifetime without coupling its value to native namespace identifiers.</summary>
    private sealed class ContextLifetime : IRHIContextLifetime
    {
        #region Public API

        /// <inheritdoc />
        public bool IsCurrentContext => !Retired;

        /// <inheritdoc />
        public bool HasRetiredOrigin => Retired;

        /// <inheritdoc />
        public void RequireContext()
        {
            if (Retired)
            {
                throw new InvalidOperationException("The original context retired.");
            }
        }

        #endregion

        #region Internal

        /// <summary>Gets or sets whether the context represented by this lifetime has retired.</summary>
        internal bool Retired;

        #endregion
    }

    #endregion
}
