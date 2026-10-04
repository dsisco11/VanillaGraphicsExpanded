using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Protects gradients introduced by engine postprocessing at the final SDR output boundary.</summary>
internal static class PbrFinalDisplayPatches
{
    #region Shader integration
    /// <summary>Imports the shared encoded-color dither without changing engine grading or lighting.</summary>
    internal static void Preprocess(SyntaxTree tree) => tree.CreateEditor()
        .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), """
        @import "./includes/pbr_color.glsl"
        uniform int vge_sceneLinear;

        """).Commit();

    /// <summary>Dithers after bloom, god rays, grading and vignettes, preserving the engine's output alpha.</summary>
    internal static void Apply(SyntaxTree tree)
    {
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).OfType<GlFunctionNode>().Single();
        var gradingQuery = Query.Keyword("vec4").FollowedBy(Query.Ident("gradedColor"));
        // Subtree selection needs explicit resolution of schema-defined keywords.
        gradingQuery.ResolveWithSchema(GlslSchema.Instance);
        var grading = gradingQuery.Select(main.Body).Single();
        var clamp = Query.Ident("color").FollowedBy(Query.Sequence(
                Query.Symbol("."), Query.Ident("rgb"), Query.Operator("="), Query.Ident("min"), Query.ParenBlock))
            .Select(main.Body).Single();

        // Linear scene/effect RGB must reach the common shoulder intact. Keep the old
        // postprocess path available as a whole when the HDR handoff is unavailable.
        tree.CreateEditor()
            .InsertBefore(clamp, "if (vge_sceneLinear == 0) ")
            .InsertBefore(grading, """
            if (vge_sceneLinear != 0) color.rgb = VgeResolveDisplay(color.rgb);

            """)
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

        // Encode and grade before quantization; HDR contributors never dither radiance.
        outColor.rgb = VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy);

        """).Commit();
    }
    #endregion
}
