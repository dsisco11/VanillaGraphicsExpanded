using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Decodes authored legacy scene color at explicit blend boundaries, preserving alpha and engine effect ownership.</summary>
internal static class SceneColorLegacyPatches
{
    #region Public API
    /// <summary>Identifies known OIT, sky and late scene sources whose RGB is authored in display units.</summary>
    internal static bool Supports(string source) => IsOit(source) || source is
        "particlescube.fsh" or "nightsky.fsh" or "celestialobject.fsh" or "decals.fsh" or "wireframe.fsh"
        or "lines.fsh" or "autocamera.fsh" or "helditem.fsh" or "woittest.fsh";

    /// <summary>Adds decoding helpers before import expansion without modifying engine shader assets.</summary>
    internal static void Preprocess(SyntaxTree tree) => tree.CreateEditor()
        .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), """
        @import "./includes/pbr_color.glsl"

        """).Commit();

    /// <summary>Converts straight RGB before premultiplication or volume integration; all non-scene draws keep legacy RGB.</summary>
    internal static void Apply(SyntaxTree tree, string source)
    {
        var editor = tree.CreateEditor();
        var header = tree.Select(Query.Syntax<GlDirectiveNode>().Named("extension")).LastOrDefault()
            ?? tree.Select(Query.Syntax<GlDirectiveNode>().Named("version")).Single();
        editor.InsertAfter(header, "\nuniform int vge_sceneLinear;\nvec3 VgeSrgbToLinear(vec3 color);\n");

        if (source == "woittest.fsh")
        {
            // The engine framebuffer diagnostic uses its older weighted OIT entry
            // point. Decode straight color before that function applies alpha/weight.
            editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("drawPixel").InnerStart("body"), """

            if (vge_sceneLinear != 0) color.rgb = VgeSrgbToLinear(color.rgb);

            """);
        }
        else if (source == "cloudvolumetric.fsh")
        {
            var traverse = tree.Select(Query.Syntax<GlFunctionNode>().Named("traverse")).OfType<GlFunctionNode>().Single();
            var declarationEnd = FindDeclarationEndOrNull(traverse.Body, "col")
                ?? throw new InvalidOperationException("Missing volumetric cloud color declaration.");
            editor.InsertAfter(declarationEnd, """

            // Decode each straight authored cloud sample before the engine integrates it.
            if (vge_sceneLinear != 0) col.rgb = VgeSrgbToLinear(col.rgb);

            """);
        }
        else if (IsOit(source))
        {
            if (!tree.Select(Query.Syntax<GlFunctionNode>().Named("OIT")).Any())
                throw new InvalidOperationException($"Missing OIT blend boundary in {source}.");
            // Keep the engine's two overloads and bucket algorithm. The macro begins
            // after their definitions, so a main call decodes exactly once before OIT.
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), """
            #if USEOIT > 0
            /** Decodes straight legacy color before engine bucket weighting. */
            void VgeLegacyOit(vec4 colour, float glow, float depth)
            {
                if (vge_sceneLinear != 0) colour.rgb = VgeSrgbToLinear(colour.rgb);
                OIT(colour, glow, depth);
            }
            /** Preserves the engine's fragment-derived depth overload. */
            void VgeLegacyOit(vec4 colour, float glow)
            {
                if (vge_sceneLinear != 0) colour.rgb = VgeSrgbToLinear(colour.rgb);
                OIT(colour, glow);
            }
            #define OIT VgeLegacyOit
            #endif

            """);
        }
        else
        {
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

            if (vge_sceneLinear != 0) outColor.rgb = VgeSrgbToLinear(outColor.rgb);

            """);
        }
        editor.Commit();
    }
    #endregion

    #region Private
    /// <summary>Separates known bucket contributors from sky and late straight-alpha draws.</summary>
    private static bool IsOit(string source) => source is "chunkliquid.fsh" or "particlesquad.fsh"
        or "particlesquad2d.fsh" or "clouds.fsh" or "cloudvolumetric.fsh" or "aurora.fsh" or "blockhighlights.fsh";

    /// <summary>Searches nested syntax without interpreting comments as declarations.</summary>
    private static SyntaxNode? FindDeclarationEndOrNull(SyntaxNode body, string name)
    {
        var children = body.Children.ToArray();
        for (int index = 0; index + 1 < children.Length; index++)
        {
            if (children[index] is SyntaxToken { Text: "vec4" }
                && children[index + 1] is SyntaxToken token && token.Text == name)
                return children.Skip(index + 2).First(node => node is SyntaxToken { Text: ";" });
        }
        foreach (var child in children)
        {
            var result = FindDeclarationEndOrNull(child, name);
            if (result is not null) return result;
        }
        return null;
    }
    #endregion
}
