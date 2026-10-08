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
            // The engine's radial blur is an authored glare effect calibrated for display
            // samples. Feeding solar radiance into it saturates the whole ray footprint.
            // Preserve that effect's response, then decode only the generated contribution
            // for addition to the untouched HDR scene at final composition.
            string body = function.ToText();
            const string sample = "texture(inputTexture, uv)";
            if (body.Split(sample, StringSplitOptions.None).Length != 3)
                throw new InvalidOperationException("Missing god-ray source sampling boundaries.");
            editor.Replace(function, """
            vec3 VgeSrgbToLinear(vec3 color);
            /** Supplies the legacy glare operator with bounded perceptual source samples. */
            vec4 VgeGodRaySource(vec2 uv)
            {
                vec4 sampleColor = texture(inputTexture, uv);
                if (vge_sceneLinear != 0) sampleColor.rgb = VgeResolveDisplay(sampleColor.rgb);
                return sampleColor;
            }

            """ + "\n" + body.Replace(sample, "VgeGodRaySource(uv)", StringComparison.Ordinal));
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

            if (vge_sceneLinear != 0) outColor.rgb = VgeSrgbToLinear(outColor.rgb);

            """);
        }
        else throw new ArgumentException("Unsupported scene postprocess source.", nameof(source));
        editor.Commit();
    }
    #endregion
}
