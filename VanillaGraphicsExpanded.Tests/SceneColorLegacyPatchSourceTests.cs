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
        string patched = tree.ToText();
        Assert.True(patched.IndexOf("bool VgeOitInputsValid(", StringComparison.Ordinal)
            < patched.IndexOf("void drawPixel(", StringComparison.Ordinal));
        // The safety/conversion prefix precedes all original alpha, depth-weight,
        // accumulation and revealage statements without changing their arithmetic.
        int originalStart = patchedBody.IndexOf("float alpha = color.a;", StringComparison.Ordinal);
        int originalBodyStart = originalBody.IndexOf("float alpha = color.a;", StringComparison.Ordinal);
        Assert.Equal(Regex.Replace(originalBody, @"\s+", ""),
            Regex.Replace(originalBody[..originalBodyStart] + patchedBody[originalStart..], @"\s+", ""));
    }

    /// <summary>Native fallback fragments retain conversion even while their dedicated VGE replacements are selected.</summary>
    [Theory]
    [InlineData("clouds.fsh")]
    [InlineData("cloudvolumetric.fsh")]
    public void RestoredCloudFragmentsRetainConditionalSceneConversion(string name)
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        string shaders = Path.Combine(root, "assets/game/shaders");
        string source = File.ReadAllText(Path.Combine(shaders, name));
        source = source.Replace("#include oit.fsh", File.ReadAllText(Path.Combine(root, "assets/game/shaderincludes/oit.fsh")));
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        ShaderCapability declared = ShaderCapability.None;
        Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, name, feature => declared |= feature));
        Assert.True(declared.HasFlag(ShaderCapability.SceneColorConvention));
        string patched = tree.ToText();
        Assert.Contains("if (vge_sceneLinear != 0)", patched);
        Assert.Contains("VgeStoreSceneLighting(VgeCompatibilityLuminance(", patched);
        if (name == "clouds.fsh")
        {
            Assert.Contains("#define OIT VgeLegacyOit", patched);
            Assert.Contains("OIT(col, 0.0, faux)", patched);
        }
        else
        {
            string traverse = tree.Select(Query.Syntax<GlFunctionNode>().Named("traverse")).OfType<GlFunctionNode>().Single().Body.ToText();
            Assert.Equal(1, traverse.Split("col.rgb = VgeStoreSceneLighting(VgeCompatibilityLuminance(col.rgb));").Length - 1);
            Assert.True(traverse.IndexOf("col.rgb =", StringComparison.Ordinal) < traverse.IndexOf("float v =", StringComparison.Ordinal));
            Assert.Contains("if (vge_sceneLinear == 0)", patched);
        }
    }

    /// <summary>The native map still generates its own color attachment and lookup-dependent controls without scene-color patching.</summary>
    [Fact]
    public void RetainedCloudMapProducerIsUnmodified()
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        var tree = SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "assets/game/shaders/cloudmap.fsh")), GlslSchema.Instance);
        string original = tree.ToText();
        Assert.False(VanillaShaderPatches.TryApplyPatches(null, tree, "cloudmap.fsh", _ => Assert.Fail("Unexpected producer capability")));
        Assert.Equal(original, tree.ToText());
    }
    #endregion
}
