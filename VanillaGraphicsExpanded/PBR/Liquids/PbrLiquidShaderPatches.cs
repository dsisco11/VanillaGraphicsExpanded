using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Replaces liquid lighting while retaining engine deformation, animated texture selection and OIT ownership.</summary>
internal static class PbrLiquidShaderPatches
{
    #region Source integration
    /// <summary>Identifies the dedicated liquid shader pair.</summary>
    internal static bool Supports(string name) => name is "chunkliquid.vsh" or "chunkliquid.fsh";

    /// <summary>Imports liquid optics before engine include expansion.</summary>
    internal static void Preprocess(SyntaxTree tree, string name)
    {
        if (name.EndsWith(".vsh", StringComparison.Ordinal)) return;
        var editor = tree.CreateEditor();
        editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), $$"""
            #define VGE_PBR_FORWARD_LUMON {{(PbrShaderLightingMode.LumOnEnabled ? 1 : 0)}}
            uniform int vge_pbrRoute;
            uniform mat4 modelViewMatrix;
            uniform sampler2D vge_materialParamsTex;
            uniform vec3 vge_atmosphereEnvironment;
            uniform vec3 vge_atmosphereSolar;
            uniform vec3 vge_atmosphereSunDirection;
            uniform vec3 vge_atmosphereAerialParams;
            in vec3 vge_viewPosition;
            in vec3 vge_blockIrradiance;
            in float vge_skyVisibility;
            @import "./includes/pbr_color.glsl"
            @import "./includes/pbr_common.glsl"
            @import "./includes/atmosphere_aerial.glsl"
            @import "./includes/pbr_liquid.glsl"

            """);
        editor.Commit();
    }

    /// <summary>Intercepts the unlit texture boundary, leaving vanilla fallback and output layout intact.</summary>
    internal static void Apply(SyntaxTree tree, string name)
    {
        var editor = tree.CreateEditor();
        var main = Query.Syntax<GlFunctionNode>().Named("main");
        if (name.EndsWith(".vsh", StringComparison.Ordinal))
        {
            editor.InsertBefore(main, """
                uniform int vge_pbrRoute;
                out vec3 vge_viewPosition;
                out vec3 vge_blockIrradiance;
                out float vge_skyVisibility;

                """);
            editor.InsertBefore(main.InnerEnd("body"), """
                vge_viewPosition = cameraPos.xyz;
                vge_blockIrradiance = max(rgbaLightIn.rgb, vec3(0));
                vge_skyVisibility = clamp(rgbaLightIn.a, 0.0, 1.0);
                // Bypass vanilla's angle-dependent alpha discard; PBR liquid optics determine final opacity.
                if (vge_pbrRoute != 0) rgba.a = 1.0;

                """);
        }
        else
        {
            var body = tree.Select(main).OfType<GlFunctionNode>().Single().Body;
            // Declarations are token siblings in TinyAst, rather than one declaration statement node.
            var children = body.Children.ToArray();
            int nameIndex = Array.FindIndex(children, node => node is SyntaxToken { Text: "rgbaFinal" });
            int typeIndex = nameIndex - 1;
            while (typeIndex >= 0 && string.IsNullOrWhiteSpace(children[typeIndex].ToText())) typeIndex--;
            if (typeIndex < 0 || children[typeIndex].ToText().Trim() != "vec4")
                throw new InvalidOperationException("Liquid unlit-material boundary is missing.");
            var boundary = children[typeIndex];
            editor.InsertBefore(boundary, """
                if (vge_pbrRoute != 0)
                {
                    vec4 material = texture(vge_materialParamsTex, uv);
                    vec4 liquid = VgeLiquidSurface(texColor, material, isLava, fullAlpha);
                    liquid = applySpheresFog(liquid, fogAmount, fWorldPos.xyz);
                    OIT(liquid, max(glowLevel, clamp(material.b, 0.0, 1.0)));
                    return;
                }

                """);
        }
        editor.Commit();
    }
    #endregion
}

