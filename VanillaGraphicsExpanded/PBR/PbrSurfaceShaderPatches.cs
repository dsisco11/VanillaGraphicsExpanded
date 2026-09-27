using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns material capture and stage-aware lighting for engine mesh and transparent programs.</summary>
internal static class PbrSurfaceShaderPatches
{
    #region Shader selection
    /// <summary>Matches source families also used by the engine's registered first-person variants.</summary>
    internal static bool Supports(string name) => name is
        "standard.vsh" or "standard.fsh" or "entityanimated.vsh" or "entityanimated.fsh"
        or "instanced.vsh" or "instanced.fsh" or "chunktransparent.vsh" or "chunktransparent.fsh";

    /// <summary>Adds shared BRDF imports before the engine import expansion step.</summary>
    internal static bool Preprocess(SyntaxTree tree, string name)
    {
        if (name == "chunktransparent.vsh")
        {
            tree.CreateEditor().InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"),
                "@import \"./includes/vge_uvrect.glsl\"\n").Commit();
            return true;
        }
        if (name.EndsWith(".vsh", StringComparison.Ordinal)) return false;
        string terrainImports = "";
        if (name == "chunktransparent.fsh")
        {
            VanillaShaderPatches.InjectPomDefines(tree);
            VanillaShaderPatches.InjectNormalMapDefines(tree);
            terrainImports = "@import \"./includes/vge_normaldepth.glsl\"\n@import \"./includes/vge_parallax.glsl\"\n";
        }
        tree.CreateEditor().InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"),
            $"#define VGE_PBR_FORWARD_LUMON {(PbrShaderLightingMode.LumOnEnabled ? 1 : 0)}\n" +
            "@import \"./includes/vsfunctions.glsl\"\n" +
            "@import \"./includes/pbr_color.glsl\"\n" +
            "@import \"./includes/pbr_common.glsl\"\n" +
            "@import \"./includes/pbr_environment.glsl\"\n" +
            "@import \"./includes/pbr_direct_brdf.glsl\"\n" +
            terrainImports + "@import \"./includes/pbr_forward_surface.glsl\"\n").Commit();
        return true;
    }
    #endregion

    #region Material and lighting integration
    /// <summary>Patches matching vertex/fragment interfaces without changing deformation, depth or coverage.</summary>
    internal static void Apply(SyntaxTree tree, string name)
    {
        bool chunk = name.StartsWith("chunktransparent", StringComparison.Ordinal);
        bool instanced = name.StartsWith("instanced", StringComparison.Ordinal);
        bool entity = name.StartsWith("entityanimated", StringComparison.Ordinal);
        bool vertex = name.EndsWith(".vsh", StringComparison.Ordinal);
        string matrix = chunk || instanced ? "modelViewMatrix" : "viewMatrix";
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
        string declarations = "\nuniform int vge_pbrRoute;\n";
        if (vertex)
        {
            if (chunk)
            {
                VanillaShaderPatches.InjectUvRectVaryings_Vsh(tree);
                VanillaShaderPatches.InjectUvRectAssign_Vsh(tree);
            }
            declarations += "out vec3 vge_viewPosition;\nout vec3 vge_blockIrradiance;\nout vec3 vge_sunIrradiance;\n";
            string tint = chunk || instanced ? "vec3(1.0)" : entity ? "renderColor.rgb * colorIn.rgb" : "rgbaTint.rgb * colorIn.rgb";
            string color = chunk ? "rgba" : "color";
            string lights = instanced ? "rgbaLightIn * rgbaBlockIn" : "rgbaLightIn";
            // Retain applyLight's alpha/glow/shadow side outputs, but never divide by lit RGB to recover tint.
            tree.CreateEditor().InsertBefore(mainQuery, declarations)
                .InsertBefore(mainQuery.InnerEnd("body"), $"""

                    vge_viewPosition = ({matrix} * worldPos).xyz;
                    vec4 vge_localLight = {lights};
                    vge_blockIrradiance = max(vge_localLight.rgb, vec3(0.0));
                    vge_sunIrradiance = max(rgbaAmbientIn, vec3(0.0)) * clamp(vge_localLight.a, 0.0, 1.0);
                    if (vge_pbrRoute != 0) {color}.rgb = {tint};

                """).Commit();
            if (!chunk && !entity && !instanced)
            {
                // The standard mesh's partial-glow tint is material appearance, not vertex illumination.
                tree.CreateEditor().InsertBefore(mainQuery.InnerEnd("body"), """

                    #if defined(GLOWSUB)
                    if (vge_pbrRoute != 0)
                    {
                        color.rgb *= 1.0 - 0.5 * gs;
                        color.rgb = mix(color.rgb, rgbaGlow.rgb, max(0.0, glowLevel - gs) / 2.0);
                    }
                    #endif

                    """).Commit();
            }
            return;
        }

        // OIT owns locations 0..5. Only non-OIT variants may declare opaque material attachments.
        // Transparent terrain's normal-map include already declares modelViewMatrix.
        if (!chunk) declarations += $"\nuniform mat4 {matrix};\n";
        declarations += $"#define VGE_SURFACE_VIEW {matrix}\n";
        declarations += """

            vec3 vge_surfaceColor = vec3(0.0);
            in vec3 vge_viewPosition;
            in vec3 vge_blockIrradiance;
            in vec3 vge_sunIrradiance;
            #if USEOIT == 0
            layout(location = 4) out vec4 vge_outNormal;
            layout(location = 5) out vec4 vge_outMaterial;
            layout(location = 6) out uvec4 vge_outPatchId;
            layout(location = 7) out vec4 vge_outEnvironment;
            #endif

            """;
        if (chunk) declarations += "uniform sampler2D vge_materialParamsTex;\nuniform sampler2D vge_normalDepthTex;\n";
        // Helpers are expanded before main, so declarations must precede the helper functions as well.
        var header = tree.Select(Query.Syntax<GlDirectiveNode>().Named("extension")).LastOrDefault()
            ?? tree.Select(Query.Syntax<GlDirectiveNode>().Named("version")).Single();
        tree.CreateEditor().InsertAfter(header, declarations).Commit();
        if (chunk)
        {
            VanillaShaderPatches.InjectUvRectVaryings_Fsh(tree);
            VanillaShaderPatches.InjectParallaxUvMapping(tree, name);
        }

        var main = tree.Select(mainQuery).OfType<GlFunctionNode>().Single();
        var children = main.Body.Children.ToArray();
        if (!chunk && !entity && !instanced)
        {
            // Preserve the authored thermal/glow tint, but do not bake the no-bloom light boost into albedo.
            var bloom = children.FirstOrDefault(node => node.ToText().TrimStart().StartsWith("#if BLOOM", StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Missing standard glow boundary in {name}.");
            tree.CreateEditor().InsertAfter(bloom, "\nif (vge_pbrRoute == 0)\n").Commit();
        }

        // Capture the actual input at the lighting boundary, independent of local names or branch layout.
        // Returning from the outer helper avoids its nested fog call capturing already modified color.
        // GUI calls retain the original implementation; main still runs coverage and depth/output code.
        string lightingEntry = !chunk && !entity && !instanced ? "applyFogAndShadow" : "applyFogAndShadowWithNormal";
        if (!tree.Select(Query.Syntax<GlFunctionNode>().Named(lightingEntry)).Any())
            throw new InvalidOperationException($"Missing lighting helper {lightingEntry} in {name}.");
        var editor = tree.CreateEditor();
        foreach (string function in new[] { "applyFog", "applyFogAndShadow", "applyFogAndShadowWithNormal", "applyFogAndShadowFromBrightness" })
            editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named(function).InnerStart("body"),
                "\nif (vge_pbrRoute != 0) { vge_surfaceColor = rgbaPixel.rgb; return rgbaPixel; }\n");
        editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("getBrightnessFromNormal").InnerStart("body"),
            "\nif (vge_pbrRoute != 0) return 1.0;\n");
        editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("applyReflectiveEffect").InnerStart("body"),
            "\nif (vge_pbrRoute != 0) return texColor;\n");
        editor.Commit();

        string parameters = chunk ? "texture(vge_materialParamsTex, uv).rgb" : "vec3(0.5, getMatMetallicFromRenderFlags(renderFlags), glowLevel)";
        string surfaceNormal = chunk
            ? "normalize(VgeComputePackedWorldNormal01Height01_WithTbn(vge_uv, normal, worldPos.xyz, vge_tbn, vge_tbnHandedness).rgb * 2.0 - 1.0)"
            : "normalize(normal)";
        string output = chunk ? "texColor" : "outColor";
        string capturedColor = !chunk && !entity && !instanced ? "vge_surfaceColor * b" : "vge_surfaceColor";
        string finish = $$"""

            #if NORMALVIEW == 0
            if (vge_pbrRoute != 0)
            {
                vec3 vge_materialColor = VgeSrgbToLinear({{capturedColor}});
                vec3 vge_params = {{parameters}};
                vec3 vge_normal = {{surfaceNormal}};
                #if USEOIT == 0
                // Late primary draws still publish defined debug/material metadata; no deferred pass follows them.
                vge_outNormal = vec4(vge_normal * 0.5 + 0.5, 1.0);
                vge_outMaterial = vec4(vge_params, vge_params.g);
                vge_outPatchId = uvec4(0u);
                vge_outEnvironment = vec4(VgeLocalEnvironment(vge_blockIrradiance, vge_sunIrradiance), 1.0);
                if (vge_pbrRoute == 1)
                {
                    {{output}}.rgb = vge_materialColor;
                }
                else
                #endif
                {
                    {{output}}.rgb = VgeForwardSurface(vge_materialColor, vge_normal, vge_params, fogAmount);
                }
            }
            #endif

            """;
        // OIT consumes a local color, so finish before that call rather than at the function end.
        main = tree.Select(mainQuery).OfType<GlFunctionNode>().Single();
        // Entity's OIT call is inside a conditional; insertion must precede that conditional
        // so the opaque variant publishes material data too.
        var oit = entity
            ? main.Body.Children.LastOrDefault(node => node.ToText().TrimStart().StartsWith("#if USEOIT > 0", StringComparison.Ordinal))
            : main.Body.Children.FirstOrDefault(node => node is SyntaxToken { Text: "OIT" });
        if ((chunk || entity) && oit is null)
            throw new InvalidOperationException($"Missing OIT publication boundary in {name}.");
        editor = tree.CreateEditor();
        if (oit is not null) editor.InsertBefore(oit, finish);
        else editor.InsertBefore(mainQuery.InnerEnd("body"), finish);
        editor.Commit();
    }

    #endregion
}
