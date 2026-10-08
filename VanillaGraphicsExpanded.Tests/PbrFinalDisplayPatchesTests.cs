using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks that final output dithering follows every installed engine postprocess operation.</summary>
public sealed class PbrFinalDisplayPatchesTests
{
    #region Final output boundary
    /// <summary>The installed final shader resolves HDR before grading and dithers only after all display effects.</summary>
    [Fact]
    public void InstalledFinalRetainsEngineBodyAndDithersLast()
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        var tree = SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "assets/game/shaders/final.fsh")), GlslSchema.Instance);
        string before = tree.ToText();
        ShaderCapability declared = ShaderCapability.None;
        Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, "final.fsh", capability => declared |= capability));
        Assert.Equal(ShaderCapability.SceneColorConvention, declared);
        string after = tree.ToText();
        const string call = "outColor.rgb = VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy);";
        Assert.Equal(1, after.Split(call).Length - 1);
        Assert.True(after.IndexOf(call, StringComparison.Ordinal) > after.LastIndexOf("outColor.a=1;", StringComparison.Ordinal));
        const string resolve = "if (vge_sceneLinear != 0) color.rgb = VgeResolveDisplay(VgeExposeCamera(color.rgb + vge_bloomContribution));";
        Assert.Equal(1, after.Split(resolve).Length - 1);
        Assert.True(after.IndexOf(resolve, StringComparison.Ordinal) < after.IndexOf("vec4 gradedColor = ColorGrade(color);", StringComparison.Ordinal));
        Assert.Matches(@"if\s*\(vge_sceneLinear\s*==\s*0\)\s*color\.rgb\s*=\s*min\(color\.rgb,\s*vec3\(1\)\);", after);
        Assert.True(VanillaShaderPatches.TryApplyPreProcessing(null, tree, "final.fsh"));
        Assert.Contains("@import \"./includes/pbr_color.glsl\"", tree.ToText());
    }
    /// <summary>Semantic anchors tolerate formatting trivia without moving the final display operations.</summary>
    [Fact]
    public void InstalledFinalAnchorsIgnoreWhitespaceAndComments()
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        string source = File.ReadAllText(Path.Combine(root, "assets/game/shaders/final.fsh"))
            .Replace("vec4 gradedColor", "vec4 /* grading anchor */ gradedColor", StringComparison.Ordinal)
            .Replace("color.rgb = min(color.rgb, vec3(1));",
                "color /* clamp anchor */ . rgb\n\t= min (color.rgb, vec3(1));", StringComparison.Ordinal);
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);

        PbrFinalDisplayPatches.Apply(tree);

        string patched = tree.ToText();
        Assert.Contains("/* grading anchor */", patched);
        Assert.Contains("/* clamp anchor */", patched);
        Assert.Equal(1, patched.Split("VgeResolveDisplay(VgeExposeCamera(color.rgb + vge_bloomContribution))").Length - 1);
        Assert.Equal(1, patched.Split("VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy)").Length - 1);
        Assert.True(patched.IndexOf("if (vge_sceneLinear == 0)", StringComparison.Ordinal)
            < patched.IndexOf("/* clamp anchor */", StringComparison.Ordinal));
        Assert.True(patched.IndexOf("VgeResolveDisplay(VgeExposeCamera(color.rgb + vge_bloomContribution))", StringComparison.Ordinal)
            < patched.IndexOf("/* grading anchor */", StringComparison.Ordinal));
    }
    #endregion
}
