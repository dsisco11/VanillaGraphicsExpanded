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
        using var texture = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, debugName: "TestImage2D");
        texture.BindImageUnit(unit: 0, access: TextureAccess.ReadWrite, level: 0, layered: false, layer: 0, format: SizedInternalFormat.Rgba8);

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

            using var bufferTexture = GpuBufferTexture.CreateWholeBuffer(bufferId, SizedInternalFormat.R32f, debugName: "TestBufferTexture");
            bufferTexture.BindImageUnit(unit: 1, access: TextureAccess.ReadOnly);

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

        using var texA = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, debugName: "ScopeTexA");
        using var texB = Texture2D.Create(4, 4, PixelInternalFormat.Rgba8, debugName: "ScopeTexB");

        // Establish a known previous binding for unit 2.
        texA.BindImageUnit(unit: 2, access: TextureAccess.ReadOnly, level: 0, layered: false, layer: 0, format: SizedInternalFormat.Rgba8);

        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 2, out int prevName);
        GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 2, out int prevAccess);
        GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 2, out int prevFormat);

        Assert.Equal(texA.TextureId, prevName);
        Assert.Equal((int)TextureAccess.ReadOnly, prevAccess);
        Assert.Equal((int)SizedInternalFormat.Rgba8, prevFormat);

        using (GpuImageUnitBinding.Bind(unit: 2, texture: texB, access: TextureAccess.ReadWrite, level: 0, layered: false, layer: 0, formatOverride: SizedInternalFormat.Rgba8))
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
