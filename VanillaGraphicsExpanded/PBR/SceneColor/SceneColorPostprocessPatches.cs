using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Adapts existing postprocess math to scene-linear inputs without replacing effect algorithms.</summary>
internal static class SceneColorPostprocessPatches
{
    #region Public API
    /// <summary>Identifies effects whose display-domain assumptions need explicit HDR handling.</summary>
    internal static bool Supports(string source) => source is "luma.fsh" or "godrays.fsh" or "colorgrade.fsh";

    /// <summary>Imports color interpretation and a default-off scene convention before engine include expansion.</summary>
    internal static void Preprocess(SyntaxTree tree) => tree.CreateEditor()
        .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), """
        @import "./includes/pbr_color.glsl"

        """).Commit();

    /// <summary>Preserves linear RGB while supplying perceptual FXAA contrast and bounded god-ray glare control.</summary>
    internal static void Apply(SyntaxTree tree, string source)
    {
        var editor = tree.CreateEditor();
        var header = tree.Select(Query.Syntax<GlDirectiveNode>().Named("extension")).LastOrDefault()
            ?? tree.Select(Query.Syntax<GlDirectiveNode>().Named("version")).Single();
        editor.InsertAfter(header, "\nuniform int vge_sceneLinear;\nvec3 VgeResolveDisplay(vec3 radiance);\n");
        if (source == "luma.fsh")
        {
            // FXAA reads perceptual contrast from alpha, but filters unexposed linear RGB.
            // This metric is not a color output conversion and does not change the scene energy.
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

            if (vge_sceneLinear != 0) outColor.a = luma(VgeResolveDisplay(outColor.rgb));

            """);
        }
        else if (source == "colorgrade.fsh")
        {
            // This alternate engine display endpoint grades a sampled scene directly.
            // Resolve only when its caller explicitly supplies scene-linear input.
            editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("ColorGrade").InnerStart("body"), """

            if (vge_sceneLinear != 0) color.rgb = VgeResolveDisplay(color.rgb);

            """);
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

            if (vge_sceneLinear != 0) outColor.rgb = VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy);

            """);
        }
        else if (source == "godrays.fsh")
        {
            var function = tree.Select(Query.Syntax<GlFunctionNode>().Named("applyGodRays")).OfType<GlFunctionNode>().Single();
            var children = function.Body.Children.ToArray();
            // The legacy factor reaches zero for sufficiently bright radiance. Use its
            // display-domain brightness metric for the existing suppression curve instead.
            var boundary = children.Select((node, index) => (node, index))
                .Single(pair => pair.node is SyntaxToken { Text: "col" } && pair.index + 3 < children.Length
                    && children[pair.index + 1].ToText().Trim() == "."
                    && children[pair.index + 2].ToText().Trim() == "rgb"
                    && children[pair.index + 3].ToText().Trim() == "*=").node;
            editor.InsertBefore(boundary, """
            if (vge_sceneLinear != 0)
            {
                vec3 displayMetric = VgeResolveDisplay(col.rgb);
                float glare = max(dot(displayMetric, vec3(1.0 / 3.0)) - 0.7, 0.0);
                col.rgb *= clamp(1.0 - glare, 0.0, 1.0);
                col.a = min(1.0, col.a);
                return col;
            }

            """);
        }
        else throw new ArgumentException("Unsupported scene postprocess source.", nameof(source));
        editor.Commit();
    }
    #endregion
}
