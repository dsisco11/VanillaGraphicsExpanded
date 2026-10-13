using System.Text.RegularExpressions;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks installed legacy shader edits without compiling runtime shader source.</summary>
public sealed class SceneColorLegacyPatchSourceTests
{
    #region Public API
    /// <summary>The native alternate grading source remains untouched by owned display composition.</summary>
    [Fact]
    public void InstalledColorGradeRemainsNative()
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        var tree = SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "assets/game/shaders/colorgrade.fsh")), GlslSchema.Instance);
        string original = tree.ToText();
        ShaderCapability declared = ShaderCapability.None;

        Assert.False(VanillaShaderPatches.TryApplyPatches(null, tree, "colorgrade.fsh", feature => declared |= feature));
        Assert.Equal(ShaderCapability.None, declared);
        Assert.Equal(original, tree.ToText());
    }
    /// <summary>The framebuffer diagnostic decodes straight RGB before retaining the engine's original weighted OIT body.</summary>
    [Fact]
    public void InstalledWoitDiagnosticDecodesBeforeAlphaWeighting()
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        var tree = SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "assets/game/shaders/woittest.fsh")), GlslSchema.Instance);
        string originalBody = tree.Select(Query.Syntax<GlFunctionNode>().Named("drawPixel"))
            .OfType<GlFunctionNode>().Single().Body.ToText();
        ShaderCapability declared = ShaderCapability.None;

        Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, "woittest.fsh", feature => declared |= feature));

        Assert.True(declared.HasFlag(ShaderCapability.SceneColorConvention));
        string patchedBody = tree.Select(Query.Syntax<GlFunctionNode>().Named("drawPixel"))
            .OfType<GlFunctionNode>().Single().Body.ToText();
        const string decode = "color.rgb = VgeStoreSceneLighting(VgeCompatibilityLuminance(color.rgb));";
        Assert.Equal(1, patchedBody.Split(decode).Length - 1);
        Assert.True(patchedBody.IndexOf(decode, StringComparison.Ordinal)
            < patchedBody.IndexOf("float alpha = color.a;", StringComparison.Ordinal));
        Assert.Contains("if (!VgeOitInputsValid(color, 0.0, 0.0)) discard;", patchedBody);
        // The safety/conversion prefix precedes all original alpha, depth-weight,
        // accumulation and revealage statements without changing their arithmetic.
        int originalStart = patchedBody.IndexOf("float alpha = color.a;", StringComparison.Ordinal);
        int originalBodyStart = originalBody.IndexOf("float alpha = color.a;", StringComparison.Ordinal);
        Assert.Equal(Regex.Replace(originalBody, @"\s+", ""),
            Regex.Replace(originalBody[..originalBodyStart] + patchedBody[originalStart..], @"\s+", ""));
    }
    #endregion
}
