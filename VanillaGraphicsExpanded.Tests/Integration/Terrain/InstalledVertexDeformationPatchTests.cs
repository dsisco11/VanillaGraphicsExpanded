using VanillaGraphicsExpanded.Tests.Integration.Support.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Checks the installed visible shader at the real runtime patch boundary without storing its source.</summary>
public sealed class InstalledVertexDeformationPatchTests
{
    #region Public API
    /// <summary>Installed entity source resolves the VGE-owned kernel and retains its material call sites.</summary>
    [Fact]
    public void InstalledEntityMaterialUsesOwnedSharedDeformation()
    {
        // Arrange and act: the helper reads the installed shader locally, runs production preprocessing and expands imports.
        string source = InstalledEngineShaderSources.Build("entityanimated.vsh", 0, 0, 0, 0, 0);

        // Assert: the resulting source contains the owned implementation and no unresolved mod import directive.
        Assert.Contains("vec4 VgeVertexDeform(VgeVertexDeformationInputs inputs, int flags, vec4 p)", source, StringComparison.Ordinal);
        Assert.Contains("return VgeVertexDeform", source, StringComparison.Ordinal);
        Assert.Contains("return VgeLiquidDeform", source, StringComparison.Ordinal);
        Assert.Contains("return VgeGlobalDeform", source, StringComparison.Ordinal);
        Assert.DoesNotContain("@import \"vanillagraphicsexpanded:shaders/includes/deformation/vertex-deformation.glsl\"", source,
            StringComparison.Ordinal);
    }
    #endregion
}
