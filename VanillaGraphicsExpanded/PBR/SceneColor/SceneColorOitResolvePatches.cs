using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Contains exceptional bucket records before the native scene resolve narrows its output.</summary>
internal static class SceneColorOitResolvePatches
{
    #region Public API
    /// <summary>Preserves bucket composition and its strict alpha cutoff while validating HDR arithmetic.</summary>
    internal static void Apply(SyntaxTree tree, SyntaxEditor editor)
    {
        // Accumulators are unnormalized FP32 sums: validate before division, without
        // imposing the narrower scene limit on their numerators or weighted alpha.
        editor.Replace(Query.Syntax<GlFunctionNode>().Named("unproject").Block("body").Inner(), """

        if (vge_sceneLinear != 0 && (any(isnan(a)) || any(isinf(a)) || any(lessThan(a, vec4(0.0))))) discard;
        vec3 result = a.w < 0.0001 ? vec3(0.0) : a.xyz / a.w;
        if (vge_sceneLinear != 0 && (any(isnan(result)) || any(isinf(result)))) discard;
        return result;

        """);
        editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"), """

        if (vge_sceneLinear != 0)
        {
            // Revealage is coverage, not lighting; rejecting invalid records preserves
            // the background rather than manufacturing a valid opaque fragment.
            if (any(isnan(reveal.xyz)) || any(isinf(reveal.xyz))
                || any(lessThan(reveal.xyz, vec3(0.0))) || any(greaterThan(reveal.xyz, vec3(1.0)))
                || isnan(anet) || isinf(anet) || anet < 0.0 || anet > 1.0) discard;
            vec3 protectedColor;
            bool clipped;
            VgeProtectLightingStorage(outColor.rgb, 0.0, VGE_STORAGE_HALF_MAX, protectedColor, clipped);
            outColor.rgb = protectedColor;
        }

        """);
    }
    #endregion
}
