using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using Xunit;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies the native selector test fixture restores image unit state across its scope.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class NativeImageSelectionTestSupportSelectorContractTests(HeadlessGLFixture fixture)
{
    #region Public API

    /// <summary>The native image selection scope restores the prior texture, access, and format tuple.</summary>
    [Fact]
    public void GpuImageUnitBinding_Scope_RestoresPreviousState()
    {
        // Arrange: create two textures and establish a known prior binding on image unit two.
        fixture.EnsureContextValid();
        fixture.MakeCurrent();
        using var texA = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription
        {
            Dimension = TextureDimension.Two,
            Format = TextureFormat.Rgba8,
            Width = System.Math.Max(1, 4),
            Height = System.Math.Max(1, 4),
            MipLevels = 1,
            Storage = ResourceStorageMode.Mutable,
            Filter = TextureFilterMode.Nearest,
            BaseLevel = 0,
            MaxLevel = (1) - 1,
            Label = "ScopeTexA"
        }).RequireResource();
        using var texB = RHICommandContext.Current.CreateTexture2D(new RHITextureDescription
        {
            Dimension = TextureDimension.Two,
            Format = TextureFormat.Rgba8,
            Width = System.Math.Max(1, 4),
            Height = System.Math.Max(1, 4),
            MipLevels = 1,
            Storage = ResourceStorageMode.Mutable,
            Filter = TextureFilterMode.Nearest,
            BaseLevel = 0,
            MaxLevel = (1) - 1,
            Label = "ScopeTexB"
        }).RequireResource();
        OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, 2, texA.TextureId, 0, false, 0,
            TextureAccess.ReadOnly, SizedInternalFormat.Rgba8);
        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 2, out int previousName);
        GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 2, out int previousAccess);
        GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 2, out int previousFormat);

        // Assert: verify the selected unit begins with the authored texture and access tuple.
        Assert.Equal(texA.TextureId, previousName);
        Assert.Equal((int)TextureAccess.ReadOnly, previousAccess);
        Assert.Equal((int)SizedInternalFormat.Rgba8, previousFormat);

        // Act: enter and leave the native selector scope with the replacement texture.
        using (new NativeImageSelection(unit: 2, texture: texB, access: TextureAccess.ReadWrite,
            level: 0, layered: false, layer: 0, formatOverride: SizedInternalFormat.Rgba8))
        {
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 2, out int selectedName);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 2, out int selectedAccess);
            GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 2, out int selectedFormat);

            // Assert: the scope selects the replacement texture with the requested access and format.
            Assert.Equal(texB.TextureId, selectedName);
            Assert.Equal((int)TextureAccess.ReadWrite, selectedAccess);
            Assert.Equal((int)SizedInternalFormat.Rgba8, selectedFormat);
        }
        GL.GetInteger((GetIndexedPName)All.ImageBindingName, 2, out int restoredName);
        GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 2, out int restoredAccess);
        GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 2, out int restoredFormat);

        // Assert: disposal restores the exact native image binding tuple.
        Assert.Equal(previousName, restoredName);
        Assert.Equal(previousAccess, restoredAccess);
        Assert.Equal(previousFormat, restoredFormat);
    }

    #endregion
}
