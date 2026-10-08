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
        @import "./includes/camera_exposure_display.glsl"
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
        // display-referred path for UI and offscreen draws of this reused executable.
        var editor = tree.CreateEditor();
        if (Query.Ident("bloomCol").Select(main.Body).Any())
        {
            var bloom = Query.Ident("color").FollowedBy(Query.Sequence(Query.Symbol("."), Query.Ident("rgb"),
                Query.Operator("="), Query.ParenBlock, Query.Operator("/"), Query.ParenBlock, Query.Symbol(";"))).Select(main.Body).Single();
            var contribution = Query.Ident("bloomSub").FollowedBy(Query.Sequence(Query.Operator("="), Query.Ident("glowLevel"),
                Query.Operator("*"), Query.ParenBlock, Query.Symbol(";"))).Select(main.Body).Single();
            // Owned bloom is already weighted radiance. Add it after SSAO rather than mixing
            // it with scene energy or allowing a blurred halo to alter the occlusion factor.
            editor.Replace(bloom, "if (vge_sceneLinear != 0) vge_bloomContribution = bloomCol.rgb; else " + bloom.ToText());
            editor.Replace(contribution, "if (vge_sceneLinear == 0) " + contribution.ToText());
        }
        editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("main").InnerStart("body"), "\nvec3 vge_bloomContribution = vec3(0);\n")
            .InsertBefore(clamp, "if (vge_sceneLinear == 0) ")
            .InsertBefore(grading, """
            if (vge_sceneLinear != 0) color.rgb = VgeResolveDisplay(VgeExposeCamera(color.rgb + vge_bloomContribution));

            """)
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

        // Encode and grade before quantization; HDR contributors never dither radiance.
        outColor.rgb = VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy);

        """).Commit();
    }
    #endregion
}
