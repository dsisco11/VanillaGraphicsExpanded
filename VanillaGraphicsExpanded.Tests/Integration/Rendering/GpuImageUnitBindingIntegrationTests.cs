using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks actual native image-unit binding through the OpenGL backend.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuImageUnitBindingIntegrationTests
{
    #region Public API

    /// <summary>Receives the serialized headless context used by native binding assertions.</summary>
    public GpuImageUnitBindingIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Image-unit bindings publish the requested texture, access, and format to native state.</summary>
    [Fact]
    public void BindImageUnit_BindsTextureAndBufferTexture()
    {
        // Arrange: create a managed two-dimensional image under the fixture's current OpenGL context.
        _fixture.EnsureContextValid();
        _fixture.MakeCurrent();

        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "TestImage2D" }).RequireResource();

        // Act: bind the two-dimensional texture to image unit zero and query native binding state.
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 0, texture.TextureId, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);

        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int boundName0);
        GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 0, out int boundAccess0);
        GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 0, out int boundFormat0);

        // Assert: the native slot exposes the exact requested name, access, and format.
        Assert.Equal(texture.TextureId, boundName0);
        Assert.Equal((int)TextureAccess.ReadWrite, boundAccess0);
        Assert.Equal((int)SizedInternalFormat.Rgba8, boundFormat0);

        // Arrange: create a real buffer-backed texture view for the second native slot.
        int bufferId = GL.GenBuffer();
        try
        {
            GL.BindBuffer(BufferTarget.TextureBuffer, bufferId);
            GL.BufferData(BufferTarget.TextureBuffer, 16 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.TextureBuffer, 0);

            using var bufferTexture = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTextureIntegration.CreateBufferView(bufferId, SizedInternalFormat.R32f, true, 0, 0, "TestBufferTexture").RequireResource();

            // Act: bind the buffer view to image unit one and read back its native selector state.
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 1, bufferTexture.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)OpenGLImageValues.Format(((GpuTextureRef)bufferTexture).RequireMetadata().Buffer!.Value.Format));

            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 1, out int boundName1);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 1, out int boundAccess1);
            GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 1, out int boundFormat1);

            // Assert: native OpenGL reports the buffer view and its metadata-derived format.
            Assert.Equal(bufferTexture.TextureId, boundName1);
            Assert.Equal((int)TextureAccess.ReadOnly, boundAccess1);
            Assert.Equal((int)SizedInternalFormat.R32f, boundFormat1);
        }
        finally
        {
            try { GL.DeleteBuffer(bufferId); } catch { }
        }
    }

    #endregion

    #region Private

    private readonly HeadlessGLFixture _fixture;

    #endregion

}
