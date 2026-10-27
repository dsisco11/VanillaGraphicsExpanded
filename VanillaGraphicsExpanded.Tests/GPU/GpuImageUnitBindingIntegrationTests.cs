using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuImageUnitBindingIntegrationTests
{
    private readonly HeadlessGLFixture _fixture;

    public GpuImageUnitBindingIntegrationTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void BindImageUnit_BindsTextureAndBufferTexture()
    {
        _fixture.EnsureContextValid();
        _fixture.MakeCurrent();

        // 2D image binding
        using var texture = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "TestImage2D" }).RequireResource();
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 0, texture.TextureId, 0, false, 0, TextureAccess.ReadWrite, SizedInternalFormat.Rgba8);

        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int boundName0);
        GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 0, out int boundAccess0);
        GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 0, out int boundFormat0);

        Assert.Equal(texture.TextureId, boundName0);
        Assert.Equal((int)TextureAccess.ReadWrite, boundAccess0);
        Assert.Equal((int)SizedInternalFormat.Rgba8, boundFormat0);

        // Buffer texture image binding
        int bufferId = GL.GenBuffer();
        try
        {
            GL.BindBuffer(BufferTarget.TextureBuffer, bufferId);
            GL.BufferData(BufferTarget.TextureBuffer, 16 * sizeof(float), IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.BindBuffer(BufferTarget.TextureBuffer, 0);

            using var bufferTexture = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLTextureIntegration.CreateBufferView(bufferId, SizedInternalFormat.R32f, true, 0, 0, "TestBufferTexture").RequireResource();
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 1, bufferTexture.TextureId, 0, false, 0, TextureAccess.ReadOnly, (SizedInternalFormat)((GpuTextureRef)bufferTexture).RequireMetadata().Buffer!.Value.Format);

            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 1, out int boundName1);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 1, out int boundAccess1);
            GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 1, out int boundFormat1);

            Assert.Equal(bufferTexture.TextureId, boundName1);
            Assert.Equal((int)TextureAccess.ReadOnly, boundAccess1);
            Assert.Equal((int)SizedInternalFormat.R32f, boundFormat1);
        }
        finally
        {
            try { GL.DeleteBuffer(bufferId); } catch { }
        }
    }

    [Fact]
    public void GpuImageUnitBinding_Scope_RestoresPreviousState()
    {
        _fixture.EnsureContextValid();
        _fixture.MakeCurrent();

        using var texA = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScopeTexA" }).RequireResource();
        using var texB = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba8, Width = System.Math.Max(1, 4), Height = System.Math.Max(1, 4), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "ScopeTexB" }).RequireResource();

        // Establish a known previous binding for unit 2.
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 2, texA.TextureId, 0, false, 0, TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);

        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 2, out int prevName);
        GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 2, out int prevAccess);
        GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 2, out int prevFormat);

        Assert.Equal(texA.TextureId, prevName);
        Assert.Equal((int)TextureAccess.ReadOnly, prevAccess);
        Assert.Equal((int)SizedInternalFormat.Rgba8, prevFormat);

        using (new VanillaGraphicsExpanded.Tests.GPU.Fixtures.NativeImageSelection(unit: 2, texture: texB, access: TextureAccess.ReadWrite, level: 0, layered: false, layer: 0, formatOverride: SizedInternalFormat.Rgba8))
        {
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 2, out int boundName);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 2, out int boundAccess);
            GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 2, out int boundFormat);

            Assert.Equal(texB.TextureId, boundName);
            Assert.Equal((int)TextureAccess.ReadWrite, boundAccess);
            Assert.Equal((int)SizedInternalFormat.Rgba8, boundFormat);
        }

        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 2, out int restoredName);
        GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 2, out int restoredAccess);
        GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 2, out int restoredFormat);

        Assert.Equal(prevName, restoredName);
        Assert.Equal(prevAccess, restoredAccess);
        Assert.Equal(prevFormat, restoredFormat);
    }
}
