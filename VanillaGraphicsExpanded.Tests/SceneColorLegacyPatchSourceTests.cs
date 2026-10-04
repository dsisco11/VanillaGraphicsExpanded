using System.Text.RegularExpressions;
using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks installed legacy shader edits without compiling runtime shader source.</summary>
public sealed class SceneColorLegacyPatchSourceTests
{
    #region Public API
    /// <summary>The alternate grading endpoint resolves linear input before grading and dithers only after output assignment.</summary>
    [Fact]
    public void InstalledColorGradeResolvesAtGradingEntry()
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        var tree = SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "assets/game/shaders/colorgrade.fsh")), GlslSchema.Instance);
        string originalBody = tree.Select(Query.Syntax<GlFunctionNode>().Named("ColorGrade"))
            .OfType<GlFunctionNode>().Single().Body.ToText();
        ShaderCapability declared = ShaderCapability.None;

        Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, "colorgrade.fsh", feature => declared |= feature));

        Assert.True(declared.HasFlag(ShaderCapability.SceneColorConvention));
        string grading = tree.Select(Query.Syntax<GlFunctionNode>().Named("ColorGrade"))
            .OfType<GlFunctionNode>().Single().Body.ToText();
        const string resolve = "if (vge_sceneLinear != 0) color.rgb = VgeResolveDisplay(color.rgb);";
        Assert.Equal(1, grading.Split(resolve).Length - 1);
        Assert.True(grading.IndexOf(resolve, StringComparison.Ordinal)
            < grading.IndexOf("color.a = dot", StringComparison.Ordinal));
        Assert.Equal(Regex.Replace(originalBody, @"\s+", ""),
            Regex.Replace(grading.Replace(resolve, "", StringComparison.Ordinal), @"\s+", ""));

        string main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main"))
            .OfType<GlFunctionNode>().Single().Body.ToText();
        const string dither = "if (vge_sceneLinear != 0) outColor.rgb = VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy);";
        Assert.Equal(1, main.Split(dither).Length - 1);
        Assert.True(main.IndexOf(dither, StringComparison.Ordinal)
            > main.IndexOf("outColor = ColorGrade(color);", StringComparison.Ordinal));
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
        const string decode = "if (vge_sceneLinear != 0) color.rgb = VgeSrgbToLinear(color.rgb);";
        Assert.Equal(1, patchedBody.Split(decode).Length - 1);
        Assert.True(patchedBody.IndexOf(decode, StringComparison.Ordinal)
            < patchedBody.IndexOf("float alpha = color.a;", StringComparison.Ordinal));
        // Apart from the new straight-color decode, retain every original alpha,
        // depth-weight, accumulation and revealage statement and comment.
        Assert.Equal(Regex.Replace(originalBody, @"\s+", ""),
            Regex.Replace(patchedBody.Replace(decode, "", StringComparison.Ordinal), @"\s+", ""));
    }
    #endregion
}
