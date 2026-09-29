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

        """).Commit();

    /// <summary>Dithers after bloom, god rays, grading and vignettes, preserving the engine's output alpha.</summary>
    internal static void Apply(SyntaxTree tree) => tree.CreateEditor()
        .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

        // Earlier scene dither cannot cover new gradients introduced by postprocessing.
        outColor.rgb = VgeDitherFinalDisplay(outColor.rgb, gl_FragCoord.xy);

        """).Commit();
    #endregion
}
