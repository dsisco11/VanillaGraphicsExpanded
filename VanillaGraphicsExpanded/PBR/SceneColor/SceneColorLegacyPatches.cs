using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Decodes authored legacy scene color at explicit blend boundaries, preserving alpha and engine effect ownership.</summary>
internal static class SceneColorLegacyPatches
{
    #region Public API
    /// <summary>Identifies retained native OIT and late scene sources whose RGB is authored in display units.</summary>
    internal static bool Supports(string source) => IsOit(source) || source is
        "particlescube.fsh" or "decals.fsh" or "wireframe.fsh"
        or "lines.fsh" or "autocamera.fsh" or "helditem.fsh" or "woittest.fsh" or "transparentcompose.fsh";

    /// <summary>Adds decoding helpers before import expansion without modifying engine shader assets.</summary>
    internal static void Preprocess(SyntaxTree tree) => tree.CreateEditor()
        .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), """
        @import "./includes/pbr_color.glsl"
        @import "./includes/pbr_scene_storage.glsl"
        @import "./includes/pbr_oit_safety.glsl"

        """).Commit();

    /// <summary>Converts straight RGB before premultiplication or volume integration; all non-scene draws keep legacy RGB.</summary>
    internal static void Apply(SyntaxTree tree, string source)
    {
        var editor = tree.CreateEditor();
        // Imported helpers can request extensions after native function definitions.
        // Publish shared declarations before the first function that can consume them.
        var firstFunction = tree.Select(Query.Syntax<GlFunctionNode>()).First();
        editor.InsertBefore(firstFunction, "\nuniform int vge_sceneLinear;\nbool VgeOitInputsValid(vec4 color, float glow, float depth);\nvec3 VgeCompatibilityLuminance(vec3 color);\nvec3 VgeStoreSceneLighting(vec3 physical);\n");
        // Imported display-noise helpers add encoded-display noise to RGB and coverage. The
        // scene path keeps both quantities clean; the final display owner still dithers.
        if (tree.Select(Query.Syntax<GlFunctionNode>().Named("NoiseFromPixelPosition")).Any())
            editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("NoiseFromPixelPosition").InnerStart("body"),
                "\nif (vge_sceneLinear != 0) return vec4(0.0);\n");

        if (source == "transparentcompose.fsh")
        {
            SceneColorOitResolvePatches.Apply(tree, editor);
        }
        else if (source == "woittest.fsh")
        {
            // The engine framebuffer diagnostic uses its older weighted OIT entry
            // point. Decode straight color before that function applies alpha/weight.
            editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("drawPixel").InnerStart("body"), """

            if (vge_sceneLinear != 0)
            {
                if (!VgeOitInputsValid(color, 0.0, 0.0)) discard;
                color.rgb = VgeStoreSceneLighting(VgeCompatibilityLuminance(color.rgb));
            }

            """);
        }
        else if (source == "cloudvolumetric.fsh")
        {
            var traverse = tree.Select(Query.Syntax<GlFunctionNode>().Named("traverse")).OfType<GlFunctionNode>().Single();
            var declarationEnd = FindDeclarationEndOrNull(traverse.Body, "col")
                ?? throw new InvalidOperationException("Missing volumetric cloud color declaration.");
            editor.InsertAfter(declarationEnd, """

            // Decode each straight authored cloud sample before the engine integrates it.
            if (vge_sceneLinear != 0)
            {
                if (any(isnan(col)) || any(isinf(col)) || col.a < 0.0 || col.a > 1.0) discard;
                col.rgb = VgeStoreSceneLighting(VgeCompatibilityLuminance(col.rgb));
            }

            """);
            // Traversal uses integer texture coordinates and convex coverage updates.
            // Reject exceptional geometry before conversion, and exceptional optical
            // samples before they can poison every bucket in the fullscreen draw.
            editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("traverse").InnerStart("body"), """

            if (vge_sceneLinear != 0 && (any(isnan(o)) || any(isinf(o))
                || any(isnan(d)) || any(isinf(d)) || any(greaterThan(abs(o.xz), vec2(2147483000.0)))
                || isnan(far) || isinf(far) || far < 0.0 || isnan(T) || isinf(T))) discard;

            """);
            editor.InsertAfter(FindDeclarationEndOrNull(traverse.Body, "v")
                ?? throw new InvalidOperationException("Missing volumetric cloud optical sample."), """

            if (vge_sceneLinear != 0 && (isnan(v) || isinf(v) || v < 0.0 || v > 1.0)) discard;

            """);
            editor.InsertAfter(FindDeclarationEndOrNull(traverse.Body, "bin")
                ?? throw new InvalidOperationException("Missing volumetric cloud bucket coordinate."), """

            if (vge_sceneLinear != 0 && (isnan(bin) || isinf(bin))) discard;

            """);
            // The sole reassignment of k in main is display noise after physical traversal.
            // Preserve that expression only for offscreen/display draws; coverage stays untouched in HDR.
            var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).OfType<GlFunctionNode>().Single();
            var children = main.Body.Children.ToArray();
            var noise = Enumerable.Range(1, Math.Max(0, children.Length - 2)).Where(index =>
                children[index] is SyntaxToken { Text: "k" } && children[index + 1] is SyntaxToken { Text: "=" }
                && children[index - 1] is not SyntaxToken { Text: "vec4" }).ToArray();
            if (noise.Length != 1) throw new InvalidOperationException("Missing unique volumetric-cloud display-noise boundary.");
            editor.InsertBefore(children[noise[0]], "if (vge_sceneLinear == 0) ");
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

            if (vge_sceneLinear != 0 && (any(isnan(k)) || any(isinf(k))
                || any(lessThan(k, vec4(0.0))) || k.a > 1.0
                || any(isnan(OITreveal)) || any(isinf(OITreveal))
                || any(lessThan(OITreveal, vec4(0.0))) || any(greaterThan(OITreveal, vec4(1.0))))) discard;

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
                if (vge_sceneLinear != 0)
                {
                    if (!VgeOitInputsValid(colour, glow, depth)) discard;
                    colour.rgb = VgeStoreSceneLighting(VgeCompatibilityLuminance(colour.rgb));
                }
                OIT(colour, glow, depth);
            }
            /** Preserves the engine's fragment-derived depth overload. */
            void VgeLegacyOit(vec4 colour, float glow)
            {
                if (vge_sceneLinear != 0)
                {
                    float depth = (gl_FragCoord.z * 2.0 - 1.0) / gl_FragCoord.w;
                    VgeLegacyOit(colour, glow, depth);
                }
                else OIT(colour, glow);
            }
            #define OIT VgeLegacyOit
            #endif

            """);
        }
        else
        {
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

            if (vge_sceneLinear != 0) outColor.rgb = VgeStoreSceneLighting(VgeCompatibilityLuminance(outColor.rgb));

            """);
        }
        editor.Commit();
    }
    #endregion

    #region Private
    /// <summary>Retains native bucket contributors for current-setting restoration, including cloud fragments and conditional liquid fallback.</summary>
    private static bool IsOit(string source) => source is "chunkliquid.fsh" or "particlesquad.fsh"
        or "particlesquad2d.fsh" or "clouds.fsh" or "cloudvolumetric.fsh" or "aurora.fsh" or "blockhighlights.fsh";

    /// <summary>Searches nested syntax without interpreting comments as declarations.</summary>
    private static SyntaxNode? FindDeclarationEndOrNull(SyntaxNode body, string name)
    {
        var children = body.Children.ToArray();
        for (int index = 0; index + 1 < children.Length; index++)
        {
            if (children[index] is SyntaxToken { Text: "vec4" or "float" }
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
