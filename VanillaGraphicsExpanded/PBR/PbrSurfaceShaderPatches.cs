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
        var editor = tree.CreateEditor();
        bool patched = Preprocess(tree, editor, name);
        if (patched) editor.Commit();
        return patched;
    }

    /// <summary>Queues shared BRDF imports into a stage-scoped transaction.</summary>
    internal static bool Preprocess(SyntaxTree tree, SyntaxEditor editor, string name)
    {
        // Shared surface sampling uses the established GL 4.5 storage contract.
        // Change the parsed directive, never scan source text.
        var version = tree.Select(Query.Syntax<GlDirectiveNode>().Named("version")).Single();
        editor.Replace(version, "#version 450 core\n");
        if (name == "chunktransparent.vsh")
        {
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"),
                """
                @import "./includes/vge_uvrect.glsl"
                @import "./includes/pbr_photometric.glsl"

                """);
            return true;
        }
        if (name.EndsWith(".vsh", StringComparison.Ordinal))
        {
            editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"),
                "@import \"./includes/pbr_photometric.glsl\"\n");
            return true;
        }
        string terrainImports = "";
        if (name == "chunktransparent.fsh")
        {
            VanillaShaderPatches.InjectNormalMapDefines(editor);
            VanillaShaderPatches.InjectPomDefines(tree, editor);
            terrainImports = """
            @import "./includes/vge_normaldepth.glsl"
            @import "./includes/vge_parallax.glsl"

            """;
        }
        editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"),
            $"""
            #define VGE_PBR_FORWARD_LUMON {(PbrShaderLightingMode.LumOnEnabled ? 1 : 0)}
            @import "./includes/vsfunctions.glsl"
            @import "./includes/pbr_color.glsl"
            @import "./includes/pbr_photometric.glsl"
            @import "./includes/pbr_common.glsl"
            @import "./includes/pbr_environment.glsl"
            @import "./includes/pbr_direct_brdf.glsl"
            @import "./includes/atmosphere_aerial.glsl"
            {terrainImports}@import "./includes/pbr_forward_surface.glsl"

            """);
        return true;
    }
    #endregion

    #region Material and lighting integration
    /// <summary>Patches matching vertex/fragment interfaces without changing deformation, depth or coverage.</summary>
    internal static void Apply(SyntaxTree tree, string name)
    {
        var editor = tree.CreateEditor();
        Apply(tree, editor, name);
        editor.Commit();
    }

    /// <summary>Queues material and lighting integration into a stage-scoped transaction.</summary>
    internal static void Apply(SyntaxTree tree, SyntaxEditor editor, string name)
    {
        QueueCoreCompatibilityEdits(tree, editor);
        bool chunk = name.StartsWith("chunktransparent", StringComparison.Ordinal);
        bool instanced = name.StartsWith("instanced", StringComparison.Ordinal);
        bool entity = name.StartsWith("entityanimated", StringComparison.Ordinal);
        bool vertex = name.EndsWith(".vsh", StringComparison.Ordinal);
        string matrix = chunk || instanced ? "modelViewMatrix" : "viewMatrix";
        string blockDecoder = chunk ? "false" : "true";
        var mainQuery = Query.Syntax<GlFunctionNode>().Named("main");
        // Surface lighting has one shadow provider. Retain defined native outputs
        // for stage linkage, but eliminate native cascade projection and sampling
        // even in variants whose engine helpers remain reachable for other effects.
        if (vertex && tree.Select(Query.Syntax<GlFunctionNode>().Named("calcShadowMapCoords")).Any())
            editor.Replace(Query.Syntax<GlFunctionNode>().Named("calcShadowMapCoords").Block("body").Inner(), """

                #if SHADOWQUALITY > 0
                shadowCoordsFar = vec4(0);
                #endif
                #if SHADOWQUALITY > 1
                shadowCoordsNear = vec4(0);
                #endif

                """);
        if (!vertex && tree.Select(Query.Syntax<GlFunctionNode>().Named("getBrightnessFromShadowMap")).Any())
            editor.Replace(Query.Syntax<GlFunctionNode>().Named("getBrightnessFromShadowMap").Block("body").Inner(),
                "\nreturn 1.0;\n");
        string declarations = """

        uniform int vge_pbrRoute;
        uniform vec3 vge_atmosphereEnvironment;
        #define VGE_ATMOSPHERE_SCATTERING_DIRECTIONS
        uniform vec3 vge_atmosphereScatteringSun;
        uniform vec3 vge_atmosphereScatteringMoon;
        uniform vec4 vge_atmosphereAerialParams;

        """;
        if (vertex)
        {
            if (chunk)
            {
                VanillaShaderPatches.InjectUvRectVaryings_Vsh(editor);
                VanillaShaderPatches.InjectUvRectAssign_Vsh(editor);
            }
            declarations += """
            out vec3 vge_viewPosition;
            out vec3 vge_blockField;
            out vec3 vge_sunIrradiance;
            out float vge_skyVisibility;

            """;
            string tint = chunk || instanced ? "vec3(1.0)" : entity ? "renderColor.rgb * colorIn.rgb" : "rgbaTint.rgb * colorIn.rgb";
            string color = chunk ? "rgba" : "color";
            string lights = instanced ? "rgbaLightIn * rgbaBlockIn" : "rgbaLightIn";
            string skyAvailability = instanced
                ? "VgeSkyAvailability(rgbaLightIn.a, true) * VgeSkyAvailability(rgbaBlockIn.a, false)"
                : $"VgeSkyAvailability(vge_localLight.a, {blockDecoder})";
                // Retain applyLight's alpha/glow/shadow side outputs, but never divide by lit RGB to recover tint.
            editor.InsertBefore(mainQuery, declarations)
                .InsertBefore(mainQuery.InnerEnd("body"), $"""

                    vge_viewPosition = ({matrix} * worldPos).xyz;
                    vec4 vge_localLight = {lights};
                    // Preserve the raw field until the fragment applies its nonlinear response.
                    vge_blockField = vge_localLight.rgb;
                    vge_skyVisibility = {skyAvailability};
                    vge_sunIrradiance = vge_atmosphereEnvironment * vge_skyVisibility;
                    if (vge_pbrRoute != 0) {color}.rgb = {tint};

                """);
            if (!chunk && !entity && !instanced)
            {
                // The standard mesh's partial-glow tint is material appearance, not vertex illumination.
                editor.InsertBefore(mainQuery.InnerEnd("body"), """

                    #if defined(GLOWSUB)
                    if (vge_pbrRoute != 0)
                    {
                        color.rgb *= 1.0 - 0.5 * gs;
                        color.rgb = mix(color.rgb, rgbaGlow.rgb, max(0.0, glowLevel - gs) / 2.0);
                    }
                    #endif

                """);
            }
            return;
        }

        // USEOIT is also defined on standard/instanced, which never publish OIT outputs.
        // Only actual OIT producers must avoid the primary material attachment locations.
        string primaryOutputs = chunk ? "0" : entity ? "(USEOIT == 0)" : "1";
        // Transparent terrain's normal-map include already declares modelViewMatrix.
        if (!chunk) declarations += $"""

        uniform mat4 {matrix};

        """;
        declarations += $"""
        #define VGE_SURFACE_VIEW {matrix}
        #define VGE_SURFACE_PRIMARY_OUTPUTS {primaryOutputs}

        """;
        declarations += """

            vec3 vge_surfaceColor = vec3(0.0);
            in vec3 vge_viewPosition;
            in vec3 vge_blockField;
            vec3 vge_blockIrradiance;
            in vec3 vge_sunIrradiance;
            in float vge_skyVisibility;
            #if VGE_SURFACE_PRIMARY_OUTPUTS
            #if defined(ALLOWDEPTHOFFSET) && ALLOWDEPTHOFFSET > 0 && SSAOLEVEL == 0
            layout(location = 3) out vec4 outGPosition;
            #endif
            layout(location = 4) out vec4 vge_outNormal;
            layout(location = 5) out vec4 vge_outMaterial;
            layout(location = 6) out uvec4 vge_outPatchId;
            layout(location = 7) out vec4 vge_outEnvironment;
            #endif

            """;
        if (chunk) declarations += """
        uniform sampler2D vge_materialParamsTex;
        uniform sampler2D vge_normalDepthTex;

        """;
        // Keep surviving header extensions before declarations, as core GLSL requires.
        // Imported or conditional directives cannot anchor unconditional material declarations.
        var header = FindMaterialHeader(tree);
        editor.InsertAfter(header, declarations);
        editor.InsertAfter(mainQuery.InnerStart("body"), $"""

            // Convert after interpolation so the curve shapes the interior of each triangle.
            vge_blockIrradiance = VgeDiffuseIncident(VgeBlockIlluminance(vge_blockField, {blockDecoder}));

            """);
        if (chunk)
        {
            VanillaShaderPatches.InjectUvRectVaryings_Fsh(editor);
            VanillaShaderPatches.InjectParallaxUvMapping(editor, name);
        }

        var main = tree.Select(mainQuery).OfType<GlFunctionNode>().Single();
        var children = main.Body.Children.ToArray();
        if (!chunk && !entity && !instanced)
        {
            // Preserve the authored thermal/glow tint, but do not bake the no-bloom light boost into albedo.
            var bloom = children.FirstOrDefault(node => node.ToText().TrimStart().StartsWith("#if BLOOM", StringComparison.Ordinal))
                ?? throw new InvalidOperationException($"Missing standard glow boundary in {name}.");
            editor.InsertAfter(bloom, """

            if (vge_pbrRoute == 0)

            """);
        }

        // Capture the actual input at the lighting boundary, independent of local names or branch layout.
        // Returning from the outer helper avoids its nested fog call capturing already modified color.
        // GUI calls retain the original implementation; main still runs coverage and depth/output code.
        string lightingEntry = !chunk && !entity && !instanced ? "applyFogAndShadow" : "applyFogAndShadowWithNormal";
        if (!tree.Select(Query.Syntax<GlFunctionNode>().Named(lightingEntry)).Any())
            throw new InvalidOperationException($"Missing lighting helper {lightingEntry} in {name}.");
        foreach (string function in new[] { "applyFog", "applyFogAndShadow", "applyFogAndShadowWithNormal", "applyFogAndShadowFromBrightness" })
            editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named(function).InnerStart("body"),
                """

                if (vge_pbrRoute != 0)
                {
                    vge_surfaceColor = rgbaPixel.rgb;
                    return rgbaPixel;
                }

                """);
        editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("getBrightnessFromNormal").InnerStart("body"),
            """

            if (vge_pbrRoute != 0) return 1.0;

            """);
        editor.InsertAfter(Query.Syntax<GlFunctionNode>().Named("applyReflectiveEffect").InnerStart("body"),
            """

            if (vge_pbrRoute != 0) return texColor;

            """);
        string parameters = chunk ? "texture(vge_materialParamsTex, uv).rgb" : "vec3(0.5, getMatMetallicFromRenderFlags(renderFlags), glowLevel)";
        string transmission = chunk ? "clamp(texture(vge_materialParamsTex, uv).a, 0.0, 1.0)" : "0.0";
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
                float vge_transmission = {{transmission}};
                vec3 vge_normal = {{surfaceNormal}};
                #if VGE_SURFACE_PRIMARY_OUTPUTS
                // Late primary draws still publish defined debug/material metadata; no deferred pass follows them.
                vge_outNormal = vec4(vge_normal * 0.5 + 0.5, 1.0);
                #if defined(ALLOWDEPTHOFFSET) && ALLOWDEPTHOFFSET > 0
                // Visibility depth uses the hand projection and bias; lighting needs the actual receiver.
                outGPosition = vec4(vge_viewPosition, 1.0);
                vge_outNormal.a = -1.0;
                #endif
                vge_outMaterial = vec4(vge_params, vge_transmission);
                vge_outPatchId = uvec4(0u);
                vge_outEnvironment = vec4(VgeLocalEnvironment(vge_blockIrradiance, vge_sunIrradiance), vge_skyVisibility);
                if (vge_pbrRoute == 1)
                {
                    // A surviving deferred fragment owns this material sample; background radiance is not albedo.
                    {{output}} = vec4(vge_materialColor, 1.0);
                }
                else
                #endif
                {
                    {{output}}.rgb = VgeForwardSurface(vge_materialColor, vge_normal, vge_params, fogAmount, vge_transmission);
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
        if (oit is not null) editor.InsertBefore(oit, finish);
        else editor.InsertBefore(mainQuery.InnerEnd("body"), finish);
    }

    #endregion

    #region Private
    /// <summary>Finds the last surviving unconditional header extension, or the version when none remains.</summary>
    private static GlDirectiveNode FindMaterialHeader(SyntaxTree tree)
    {
        var header = tree.Select(Query.Syntax<GlDirectiveNode>().Named("version")).OfType<GlDirectiveNode>().Single();
        // Inspect only the leading unconditional directive prefix. Selecting an extension inside an
        // imported include guard could suppress material declarations or place them after helpers.
        foreach (var node in tree.Root.Children)
        {
            if (node is not GlDirectiveNode directive || directive.Name is "if" or "ifdef" or "ifndef") break;
            if (directive.Name == "extension" && !IsRedundantCoreExtension(directive)) header = directive;
        }
        return header;
    }

    /// <summary>Identifies the compatibility extension directives already supplied by the selected core language.</summary>
    private static bool IsRedundantCoreExtension(GlDirectiveNode directive)
    {
        string extension = directive.ArgumentsText.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return extension is "GL_ARB_shading_language_420pack" or "GL_ARB_shader_storage_buffer_object" or
            "GL_ARB_shader_image_load_store" or "GL_ARB_explicit_attrib_location";
    }

    /// <summary>Queues core-profile compatibility edits alongside the material and lighting changes.</summary>
    private static void QueueCoreCompatibilityEdits(SyntaxTree tree, SyntaxEditor editor)
    {
        // The shared editor resolves adjacent replacements and header insertions from the same tree.
        // Keep unrelated extension directives and native expression types unchanged.
        foreach (var directive in tree.Select(Query.Syntax<GlDirectiveNode>().Named("extension")).OfType<GlDirectiveNode>())
        {
            if (IsRedundantCoreExtension(directive))
                editor.Replace(directive, "\n");
        }
        QueueFloatClampLiterals(tree, editor);
    }

    /// <summary>Keeps native scalar clamp calls unambiguously float when core GLSL also offers double overloads.</summary>
    private static void QueueFloatClampLiterals(SyntaxTree tree, SyntaxEditor editor)
    {
        foreach (var (functionName, variable) in new[] { ("applyPsychedelicEffect", "df"), ("getFogLevel", "nearnessToPlayer") })
        foreach (var function in tree.Select(Query.Syntax<GlFunctionNode>().Named(functionName)).OfType<GlFunctionNode>())
        {
            // Match the typed declaration, then only the scalar bound arguments of
            // its clamp call. Integer loops, other expressions and native effects
            // keep their original types and behavior.
            // Function bodies retain these declarations as flat AST tokens.
            var nodes = function.Body.Children.ToArray();
            int declaration = Enumerable.Range(0, Math.Max(0, nodes.Length - 4)).Single(index =>
                nodes[index] is SyntaxToken { Text: "float" } &&
                nodes[index + 1] is SyntaxToken name && name.Text == variable &&
                nodes[index + 2] is SyntaxToken { Text: "=" } &&
                nodes[index + 3] is SyntaxToken { Text: "clamp" } &&
                nodes[index + 4] is SyntaxBlock { Opener: '(' });
            var arguments = (SyntaxBlock)nodes[declaration + 4];
            int argument = 0;
            foreach (var node in arguments.Children.Skip(1).SkipLast(1))
            {
                if (node is SyntaxToken { Text: "," }) { argument++; continue; }
                if (argument > 0 && node is SyntaxToken token && token.Text is "0" or "1")
                    editor.Replace(token, token.Text + ".0");
            }
        }
    }
    #endregion
}
