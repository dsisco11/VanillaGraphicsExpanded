using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Terrain;

/// <summary>Verifies runtime surface patching shares the authored bounded deformation implementation.</summary>
public sealed class VertexDeformationPatchesTests
{
    #region Public API
    /// <summary>Supported visible material entry points import the shared model and preserve call order.</summary>
    [Theory]
    [InlineData("chunkopaque.vsh")]
    [InlineData("chunktopsoil.vsh")]
    [InlineData("chunktransparent.vsh")]
    [InlineData("chunkshadowmap.vsh")]
    [InlineData("entityanimated.vsh")]
    [InlineData("shadowmapentityanimated.vsh")]
    [InlineData("standard.vsh")]
    [InlineData("instanced.vsh")]
    public void SupportedMaterialUsesTheSharedAuthoredKernel(string sourceName)
    {
        // Arrange: use an authored fixture with independent local, liquid, global, and perception calls.
        var tree = SyntaxTree.Parse("""
            vec4 worldPos;
            vec4 applyVertexWarping(vec4 worldPos, int renderFlags) { return worldPos + 10.0; }
            vec4 applyLiquidWarping(bool windAffected, vec4 worldPos, float div) { return worldPos + 20.0; }
            vec4 applyGlobalWarping(vec4 worldPos) { return worldPos + 30.0; }
            vec3 applyPerceptionWarping(vec3 worldPos) { return worldPos + vec3(40.0); }
            void main() { worldPos = applyGlobalWarping(worldPos); }
            """, GlslSchema.Instance);

        // Act: run the same preprocessing and AST patch hooks used by visible surface shaders.
        bool imported = VertexDeformationPatches.Preprocess(tree, sourceName);
        bool applied = VertexDeformationPatches.Apply(tree, sourceName);
        string result = tree.ToText();

        // Assert: call sites remain, while each implementation delegates to the owned shared model.
        Assert.True(imported);
        Assert.True(applied);
        Assert.Contains("@import \"vanillagraphicsexpanded:shaders/includes/deformation/vertex-deformation.glsl\"", result, StringComparison.Ordinal);
        Assert.Contains("return VgeVertexDeform(VgeVisibleDeformationInputs(), renderFlags, worldPos);", result, StringComparison.Ordinal);
        Assert.Contains("return VgeLiquidDeform(VgeVisibleDeformationInputs(), windAffected, worldPos, div);", result, StringComparison.Ordinal);
        Assert.Contains("return VgeGlobalDeform(VgeVisibleDeformationInputs(), worldPos);", result, StringComparison.Ordinal);
        Assert.Contains("return VgePerceptionDeform(VgeVisibleDeformationInputs(), worldPos);", result, StringComparison.Ordinal);
        Assert.Contains("worldPos = applyGlobalWarping(worldPos);", result, StringComparison.Ordinal);
        Assert.DoesNotContain("return worldPos + 10.0", result, StringComparison.Ordinal);
        Assert.DoesNotContain("return worldPos + 20.0", result, StringComparison.Ordinal);
        Assert.DoesNotContain("return worldPos + 30.0", result, StringComparison.Ordinal);
    }

    /// <summary>Unsupported shader families and materials without deformation remain unchanged.</summary>
    [Fact]
    public void UnsupportedOrUnwarpedMaterialIsLeftAlone()
    {
        // Arrange: pair an unsupported name with a supported source without the deformation interface.
        var unsupported = SyntaxTree.Parse("vec4 applyVertexWarping(){return vec4(0);}", GlslSchema.Instance);
        var unwarped = SyntaxTree.Parse("void main(){gl_Position=vec4(0);}", GlslSchema.Instance);
        string unsupportedBefore = unsupported.ToText();
        string unwarpedBefore = unwarped.ToText();

        // Act: apply both hooks to each boundary case.
        bool unsupportedImport = VertexDeformationPatches.Preprocess(unsupported, "unsupported.vsh");
        bool unsupportedPatch = VertexDeformationPatches.Apply(unsupported, "unsupported.vsh");
        bool unwarpedImport = VertexDeformationPatches.Preprocess(unwarped, "standard.vsh");
        bool unwarpedPatch = VertexDeformationPatches.Apply(unwarped, "standard.vsh");

        // Assert: unmatched inputs receive no shared import or implementation rewrite.
        Assert.False(unsupportedImport);
        Assert.False(unsupportedPatch);
        Assert.False(unwarpedImport);
        Assert.False(unwarpedPatch);
        Assert.Equal(unsupportedBefore, unsupported.ToText());
        Assert.Equal(unwarpedBefore, unwarped.ToText());
    }

    /// <summary>The patch chain declares shared deformation only when the material implementation was actually replaced.</summary>
    [Fact]
    public void UnmatchedMaterialDoesNotDeclareTheSharedShaderCapability()
    {
        // Arrange: unsupported material code still has a coincidentally named function but is outside the patch list.
        var tree = SyntaxTree.Parse("vec4 applyVertexWarping(){return vec4(0);}", GlslSchema.Instance);
        ShaderCapability? declared = null;

        // Act: run the production patch chain and observe its capability publication.
        bool applied = VanillaShaderPatches.TryApplyPatches(null, tree, "unsupported.vsh",
            capability => declared = (declared ?? ShaderCapability.None) | capability);

        // Assert: this shader receives no rewrite and no capability that promises a shared implementation.
        Assert.False(applied);
        Assert.Null(declared);
        Assert.DoesNotContain("VgeVertexDeform", tree.ToText(), StringComparison.Ordinal);
    }

    /// <summary>A patched engine entity shadow stage advertises shared deformation to the capability cache.</summary>
    [Fact]
    public void EntityShadowPatchDeclaresSharedDeformationCapability()
    {
        // Arrange: provide only the deformation function contract consumed by the installed shadow stage.
        var tree = SyntaxTree.Parse("vec4 applyVertexWarping(vec4 worldPos,int renderFlags){return worldPos;} void main(){}",
            GlslSchema.Instance);
        ShaderCapability? declared = null;

        // Act: route the authored entity-shadow source through the engine patch dispatcher.
        bool applied = VanillaShaderPatches.TryApplyPatches(null, tree, "shadowmapentityanimated.vsh",
            capability => declared = (declared ?? ShaderCapability.None) | capability);

        // Assert: the capability appears only when the shared implementation is installed successfully.
        Assert.True(applied);
        Assert.Equal(ShaderCapability.SharedVertexDeformation, declared);
        Assert.Contains("VgeVertexDeform", tree.ToText(), StringComparison.Ordinal);
    }
    #endregion
}
