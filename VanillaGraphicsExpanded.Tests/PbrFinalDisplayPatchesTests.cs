using TinyTokenizer.Ast;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks that final output dithering follows every installed engine postprocess operation.</summary>
public sealed class PbrFinalDisplayPatchesTests
{
    #region Final output boundary
    /// <summary>The original final shader remains intact and receives only one RGB-only trailing operation.</summary>
    [Fact]
    public void InstalledFinalRetainsEngineBodyAndDithersLast()
    {
        string root = Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? "G:/Vintagestory";
        var tree = SyntaxTree.Parse(File.ReadAllText(Path.Combine(root, "assets/game/shaders/final.fsh")), GlslSchema.Instance);
        string before = tree.ToText();
        Assert.True(VanillaShaderPatches.TryApplyPatches(null, tree, "final.fsh"));
        string after = tree.ToText();
        const string call = "outColor.rgb = VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy);";
        Assert.Equal(1, after.Split(call).Length - 1);
        Assert.True(after.IndexOf(call, StringComparison.Ordinal) > after.LastIndexOf("outColor.a=1;", StringComparison.Ordinal));
        Assert.DoesNotContain("VgeResolveDisplay", after);
        int comment = after.IndexOf("// Earlier scene dither", StringComparison.Ordinal);
        int end = after.IndexOf(call, StringComparison.Ordinal) + call.Length;
        Assert.Equal(before.Where(c => !char.IsWhiteSpace(c)), after.Remove(comment, end - comment).Where(c => !char.IsWhiteSpace(c)));
        Assert.True(VanillaShaderPatches.TryApplyPreProcessing(null, tree, "final.fsh"));
        Assert.Contains("@import \"./includes/pbr_color.glsl\"", tree.ToText());
    }
    #endregion
}


