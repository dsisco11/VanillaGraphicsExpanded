using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Protects native prelit particle and decal normalization when scene ambient lighting is black.</summary>
internal static class SceneColorParticleLightingPatches
{
    #region Public API
    /// <summary>Selects the native vertex consumers whose prelit color enters the compatibility scene route.</summary>
    internal static bool Supports(string source) => source is "particlescube.vsh" or "decals.vsh";

    /// <summary>Retains native offscreen arithmetic while giving scene normalization a finite zero-light limit.</summary>
    internal static void Apply(SyntaxTree tree)
    {
        var editor = tree.CreateEditor();
        var firstFunction = tree.Select(Query.Syntax<GlFunctionNode>()).First();
        editor.InsertBefore(firstFunction, """

        uniform int vge_sceneLinear;
        /** Scales source colors and weights before mixing, then restores brightness without a dark RGB reciprocal. */
        vec3 VgeRestoreLegacyLighting(vec3 sun, vec3 block, vec3 point,
            float sunWeight, float blockWeight, float pointWeight, float brightness)
        {
            float colorScale = max(max(sun.r, max(sun.g, sun.b)),
                max(max(block.r, max(block.g, block.b)), max(point.r, max(point.g, point.b))));
            float weightScale = max(sunWeight, max(blockWeight, pointWeight));
            if (colorScale <= 0.0 || weightScale <= 0.0) return vec3(0.0);
            // Normal divisors also give a finite fallback on implementations that
            // flush subnormal arithmetic. Ordinary nonzero source ratios cancel.
            colorScale = max(colorScale, 1.175494351e-38);
            weightScale = max(weightScale, 1.175494351e-38);
            vec3 color = (sun / colorScale) * (sunWeight / weightScale)
                + (block / colorScale) * (blockWeight / weightScale)
                + (point / colorScale) * (pointWeight / weightScale);
            float peak = max(color.r, max(color.g, color.b));
            if (peak < 1.175494351e-38) return vec3(0.0);
            vec3 chromaticity = color / peak;
            return chromaticity * (brightness / ((chromaticity.r + chromaticity.g + chromaticity.b) / 3.0));
        }

        """);
        // Only these two inspected native functions perform the weighted mean and
        // subsequent brightness restoration. Fail explicitly if their source moves.
        Rewrite(tree, editor, "applyLightWithoutPointLight",
            "(2 * bSun + bBlock)",
            "rgba *= max(bGlow, max(bSun, bBlock)) / ((rgba.r + rgba.g + rgba.b) / 3);",
            "rgba.rgb = VgeRestoreLegacyLighting(sunColor.rgb, blockColor.rgb, vec3(0.0), 2.0*bSun, bBlock, 0.0, max(bGlow, max(bSun, bBlock)));");
        Rewrite(tree, editor, "applyLight", "(2 * bSun + bBlock + bPoint)",
            "rgba *= bMax / ((rgba.r + rgba.g + rgba.b) / 3);",
            "rgba = VgeRestoreLegacyLighting(sunLightColor, blockLightColor, pointColSum.rgb, 2.0*bSun, bBlock, bPoint, bMax);");
        editor.Commit();
    }
    #endregion

    #region Private
    /// <summary>Replaces verified expression boundaries without changing the surrounding native lighting policy.</summary>
    private static void Rewrite(SyntaxTree tree, SyntaxEditor editor, string name,
        string denominator, string normalization, string safeNormalization)
    {
        var function = tree.Select(Query.Syntax<GlFunctionNode>().Named(name)).OfType<GlFunctionNode>().Single();
        string body = function.Body.ToText().Trim();
        if (body.Split(denominator, StringSplitOptions.None).Length != 2
            || body.Split(normalization, StringSplitOptions.None).Length != 2)
            throw new InvalidOperationException($"Unsupported native particle normalization in {name}.");
        if (body.StartsWith('{') && body.EndsWith('}')) body = body[1..^1];
        // The weighted color is zero when all inputs are zero. A normal positive
        // denominator prevents 0/0 (including flushed tiny products); the later
        // peak normalization restores brightness only when chromaticity exists.
        string weightSum = denominator[1..^1];
        body = body.Replace(denominator,
            $"(vge_sceneLinear != 0 ? max({weightSum}, 1.175494351e-38) : {denominator})",
            StringComparison.Ordinal).Replace(normalization,
            $"if (vge_sceneLinear != 0) {{ {safeNormalization} }} else {{ {normalization} }}",
            StringComparison.Ordinal);
        editor.Replace(Query.Syntax<GlFunctionNode>().Named(name).Block("body").Inner(), body);
    }
    #endregion
}
